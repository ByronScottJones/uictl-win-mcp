using System.Text.Json;

namespace UICtl.Core;

/// <summary>One local feedback entry - an issue, error, or recommendation about uictl itself.</summary>
public sealed record FeedbackEntry(
    int Id,
    string Category,
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Status,
    DateTimeOffset? SubmittedAt,
    string? SubmittedUrl);

/// <summary>
/// Local-first storage for feedback about uictl itself, before a separate
/// `submit` step hands a specific entry off to GitHub. One JSON file
/// (%LOCALAPPDATA%\uictl\feedback.json, {nextId, entries}), read-modify-
/// written on every call under a lock - feedback CRUD isn't a hot path the
/// way the daemon's element cache is, so there's no in-memory cache to keep
/// coherent, just serialization against two requests racing on the file.
/// Mirrors macOS's FeedbackStore.swift.
/// </summary>
public static class FeedbackStore
{
    public static readonly IReadOnlyList<string> Categories = ["issue", "error", "recommendation"];

    private static readonly object Lock = new();
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uictl", "feedback.json");

    private static readonly JsonSerializerOptions FileJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed class FileContents
    {
        public int NextId { get; set; } = 1;
        public List<FeedbackEntry> Entries { get; set; } = [];
    }

    public static FeedbackEntry Create(string category, string title, string body)
    {
        ValidateCategory(category);
        lock (Lock)
        {
            var contents = Load();
            var now = DateTimeOffset.UtcNow;
            var entry = new FeedbackEntry(contents.NextId, category, title, body, now, now, "draft", null, null);
            contents.Entries.Add(entry);
            contents.NextId++;
            Save(contents);
            return entry;
        }
    }

    public static IReadOnlyList<FeedbackEntry> List()
    {
        lock (Lock) return Load().Entries;
    }

    public static FeedbackEntry Get(int id)
    {
        lock (Lock) return Find(id, Load());
    }

    public static FeedbackEntry Update(int id, string? category, string? title, string? body)
    {
        if (category is not null) ValidateCategory(category);
        lock (Lock)
        {
            var contents = Load();
            int index = IndexOf(id, contents);
            var updated = contents.Entries[index] with
            {
                Category = category ?? contents.Entries[index].Category,
                Title = title ?? contents.Entries[index].Title,
                Body = body ?? contents.Entries[index].Body,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            contents.Entries[index] = updated;
            Save(contents);
            return updated;
        }
    }

    public static void Delete(int id)
    {
        lock (Lock)
        {
            var contents = Load();
            contents.Entries.RemoveAt(IndexOf(id, contents));
            Save(contents);
        }
    }

    /// <summary>
    /// Marks an entry submitted without opening anything - used once a URL
    /// has already been handed to (and presumably opened by) an MCP client
    /// via URL-mode elicitation, so the daemon doesn't also open its own
    /// browser tab for the same submission.
    /// </summary>
    public static FeedbackEntry MarkSubmitted(int id, string url)
    {
        lock (Lock)
        {
            var contents = Load();
            int index = IndexOf(id, contents);
            var updated = contents.Entries[index] with { Status = "submitted", SubmittedAt = DateTimeOffset.UtcNow, SubmittedUrl = url };
            contents.Entries[index] = updated;
            Save(contents);
            return updated;
        }
    }

    /// <summary>
    /// GitHub natively pre-fills a new issue's title/body from query params.
    /// The category has no matching field there, so it's folded into the body.
    /// </summary>
    public static Uri SubmissionUrl(FeedbackEntry entry, string repo)
    {
        string body = $"**Category:** {entry.Category}\n\n{entry.Body}";
        string query = $"title={Uri.EscapeDataString(entry.Title)}&body={Uri.EscapeDataString(body)}";
        return new Uri($"https://github.com/{repo}/issues/new?{query}");
    }

    private static void ValidateCategory(string category)
    {
        if (!Categories.Contains(category))
            throw new UiCtlException($"\"category\" must be one of: {string.Join(", ", Categories)}");
    }

    private static int IndexOf(int id, FileContents contents)
    {
        int index = contents.Entries.FindIndex(e => e.Id == id);
        if (index < 0) throw new UiCtlException($"no feedback entry with id {id}");
        return index;
    }

    private static FeedbackEntry Find(int id, FileContents contents) => contents.Entries[IndexOf(id, contents)];

    private static FileContents Load()
    {
        if (!File.Exists(FilePath)) return new FileContents();
        string json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<FileContents>(json, FileJsonOptions)
            ?? throw new UiCtlException("failed to parse feedback.json");
    }

    /// <summary>
    /// Writes to a temp file and renames it into place, so a crash or kill
    /// mid-write can't leave a half-written, corrupt feedback.json - the file
    /// is either the old contents or the new ones, never a partial mix of
    /// both. Mirrors macOS's use of Data.write(options: .atomic).
    /// </summary>
    private static void Save(FileContents contents)
    {
        string dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        string tempPath = Path.Combine(dir, $".feedback.json.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, JsonSerializer.Serialize(contents, FileJsonOptions));
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
