using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UICtl.Core;

public sealed record ActivityEntry(
    DateTimeOffset Timestamp,
    string Command,
    string ParamsSummary,
    string ResponseSummary,
    bool Ok,
    double DurationMs);

/// <summary>
/// Records every CLI/MCP call the daemon handles, so a human watching this
/// machine can see what's being automated (`uictl log show`) and an agent can
/// export an audit trail (`uictl log export`). Capped at <see cref="MaxEntries"/>
/// (oldest dropped first) since this lives for the daemon's whole lifetime.
/// Mirrors macOS's ActivityLog.swift.
/// </summary>
public static class ActivityLog
{
    /// <summary>Internal (not private) so the GUI layer's on-screen row list can cap itself at the same size - see ActivityLogWindow.Append.</summary>
    internal const int MaxEntries = 2000;

    private static readonly object Lock = new();
    private static readonly List<ActivityEntry> Entries = [];

    // UnsafeRelaxedJsonEscaping here (and in SummaryJsonOptions below) - the
    // default encoder escapes '+', '<', '>', '&', etc. as literal \uXXXX
    // text for HTML-embedding safety, which this log/export never needs and
    // which otherwise turns e.g. the "<N chars>" redaction placeholder or a
    // "ctrl+shift+esc" key combo into unreadable escape sequences on screen
    // and in the exported file.
    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions SummaryJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Invoked after every record - wired up once at daemon startup by the
    /// GUI layer (toast + log window), so this type itself has no WPF
    /// dependency. Unlike macOS's onRecord (which itself dispatches to the
    /// main thread via DispatchQueue.main.async), this is invoked directly
    /// on the calling thread; the GUI-side subscriber is responsible for its
    /// own Dispatcher.BeginInvoke marshaling, keeping this type UI-framework
    /// agnostic.
    /// </summary>
    public static Action<ActivityEntry>? OnRecord { get; set; }

    /// <summary>Invoked by "log.show" to bring the activity log window to the front - see the same doc note above about GUI wiring.</summary>
    public static Action? ShowWindow { get; set; }

    public static void Record(string command, JsonElement @params, string responseJson, double durationMs)
    {
        using var doc = JsonDocument.Parse(responseJson);
        bool ok = doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();

        var entry = new ActivityEntry(
            DateTimeOffset.UtcNow,
            command,
            SummarizeParams(command, @params),
            SummarizeResponse(command, doc.RootElement),
            ok,
            durationMs);

        lock (Lock)
        {
            Entries.Add(entry);
            if (Entries.Count > MaxEntries)
                Entries.RemoveRange(0, Entries.Count - MaxEntries);
        }

        OnRecord?.Invoke(entry);
    }

    public static IReadOnlyList<ActivityEntry> Snapshot()
    {
        lock (Lock) return Entries.ToList();
    }

    public static void ExportJson(string path)
    {
        var records = Snapshot().Select(e => new Dictionary<string, object?>
        {
            ["timestamp"] = e.Timestamp,
            ["command"] = e.Command,
            ["params"] = e.ParamsSummary,
            ["response"] = e.ResponseSummary,
            ["ok"] = e.Ok,
            ["durationMs"] = e.DurationMs,
        }).ToList();

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(records, ExportJsonOptions));
    }

    /// <summary>
    /// Redacts "text" to a length placeholder for commands that type or set
    /// the clipboard - that's the one param shape likely to carry something
    /// sensitive (a password typed into a field, secret clipboard content),
    /// and this log is both on-screen and exportable to a file.
    ///
    /// Deliberately not redacted: ocr/elements/screenshot output, whose
    /// entire purpose is reading whatever's genuinely on screen - which can
    /// include sensitive text an automated app happens to display. See
    /// MCP_INTERFACE.md's feedback/log security notes before treating the
    /// log window, its exports, or saved screenshots as safe to leave lying
    /// around.
    /// </summary>
    private static string SummarizeParams(string command, JsonElement @params)
    {
        JsonNode? node = @params.ValueKind == JsonValueKind.Undefined ? null : JsonNode.Parse(@params.GetRawText());
        if ((command == "type" || command == "clipboard.set") && node is JsonObject obj &&
            obj["text"]?.GetValue<string>() is { } text)
        {
            obj["text"] = $"<{text.Length} chars>";
        }
        return JsonSummary(node);
    }

    /// <summary>Same redaction, mirrored for the one response shape that returns clipboard content back out (clipboard.get).</summary>
    private static string SummarizeResponse(string command, JsonElement envelope)
    {
        JsonNode? envNode = JsonNode.Parse(envelope.GetRawText());
        JsonNode? payload = envNode?["data"] ?? envNode?["error"];
        if (command == "clipboard.get" && payload is JsonObject dataObj &&
            dataObj["text"]?.GetValue<string>() is { } text)
        {
            dataObj["text"] = $"<{text.Length} chars>";
        }
        return JsonSummary(payload);
    }

    /// <summary>JSON-encodes a possibly-bare fragment (string, number, null - not just an object/array) for display/export, truncated to a sane length.</summary>
    private static string JsonSummary(JsonNode? node)
    {
        const int limit = 4000;
        string str = node?.ToJsonString(SummaryJsonOptions) ?? "null";
        return str.Length > limit ? str[..limit] + "…" : str;
    }
}
