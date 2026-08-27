using System.Text.Json;
using UICtl.Core;

namespace UICtl.Core.Tests;

/// <summary>
/// ActivityLog.Entries is process-wide static state with no reset hook, so
/// these tests only ever assert forward-looking things (a freshly-recorded
/// entry appears somewhere in the snapshot, cap behavior holds after adding
/// a known number more) rather than the exact count or index - other tests,
/// and the daemon's own real traffic if this ran against a live process,
/// keep adding to the same list.
/// </summary>
public class ActivityLogTests
{
    private static JsonElement Params(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Record_AppearsInSnapshot_WithOkAndDuration()
    {
        string marker = $"activitylog-test-{Guid.NewGuid():N}";
        ActivityLog.Record(marker, Params("""{"a":1}"""), """{"ok":true,"data":{"b":2}}""", 12.5);

        var entry = Assert.Single(ActivityLog.Snapshot(), e => e.Command == marker);
        Assert.True(entry.Ok);
        Assert.Equal(12.5, entry.DurationMs);
        Assert.Equal("""{"a":1}""", entry.ParamsSummary);
        Assert.Equal("""{"b":2}""", entry.ResponseSummary);
    }

    [Fact]
    public void Record_FailureResponse_SummarizesTheErrorNotOk()
    {
        string marker = $"activitylog-test-{Guid.NewGuid():N}";
        ActivityLog.Record(marker, Params("{}"), """{"ok":false,"error":"boom"}""", 1);

        var entry = Assert.Single(ActivityLog.Snapshot(), e => e.Command == marker);
        Assert.False(entry.Ok);
        Assert.Equal("\"boom\"", entry.ResponseSummary);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("clipboard.set")]
    public void Record_RedactsTextParamForTypeAndClipboardSet(string command)
    {
        // Redaction switches on the literal command name, so this can't use
        // a per-test unique marker like the other tests here - .Last() picks
        // out the entry this call just added instead.
        ActivityLog.Record(command, Params("""{"text":"super secret password"}"""), """{"ok":true,"data":{}}""", 1);

        var entry = ActivityLog.Snapshot().Last(e => e.Command == command);
        Assert.Contains("<21 chars>", entry.ParamsSummary);
        Assert.DoesNotContain("secret", entry.ParamsSummary);
    }

    [Fact]
    public void Record_RedactsTextInClipboardGetResponse()
    {
        ActivityLog.Record("clipboard.get", Params("{}"), """{"ok":true,"data":{"text":"secret clipboard content"}}""", 1);

        var entry = ActivityLog.Snapshot().Last(e => e.Command == "clipboard.get");
        Assert.Contains("<24 chars>", entry.ResponseSummary);
        Assert.DoesNotContain("secret", entry.ResponseSummary);
    }

    [Fact]
    public void Record_DoesNotRedactOtherCommandsTextParam()
    {
        string marker = $"key-{Guid.NewGuid():N}";
        ActivityLog.Record(marker, Params("""{"combo":"ctrl+shift+esc"}"""), """{"ok":true,"data":{"sent":"ctrl+shift+esc"}}""", 1);

        var entry = Assert.Single(ActivityLog.Snapshot(), e => e.Command == marker);
        Assert.Contains("ctrl+shift+esc", entry.ParamsSummary);
    }

    [Fact]
    public void ExportJson_WritesWellFormedArrayIncludingRecentEntries()
    {
        string marker = $"export-test-{Guid.NewGuid():N}";
        ActivityLog.Record(marker, Params("{}"), """{"ok":true,"data":{"exported":true}}""", 3.25);

        string path = Path.Combine(Path.GetTempPath(), $"uictl-activitylog-test-{Guid.NewGuid():N}", "export.json");
        try
        {
            ActivityLog.ExportJson(path);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Contains(doc.RootElement.EnumerateArray(), e => e.GetProperty("command").GetString() == marker);
        }
        finally
        {
            string? dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
