using System.Text.Json;
using UICtl.Core.Tests.TestSupport;
using UICtl.Ipc;

namespace UICtl.Core.Tests;

/// <summary>
/// Dispatch-level tests for the commands added in Phases 1-2, per TESTING.md's
/// own suggested shortcut: call CommandDispatcher.Dispatch directly, no
/// daemon/pipe needed. Asserts on the actual envelope JSON, since that's what
/// every front end (CLI, MCP) ultimately receives.
/// </summary>
public class CommandDispatcherTests
{
    private static JsonElement Params(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static readonly JsonElement Empty = Params("{}");

    [Fact]
    public void DisplaysList_ReturnsOkWithAtLeastOneDisplay()
    {
        string envelope = CommandDispatcher.Dispatch("displays.list", Empty);
        using var doc = JsonDocument.Parse(envelope);

        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("data").GetProperty("displays").GetArrayLength() > 0);
    }

    [Fact]
    public void UnknownCommand_ReturnsFailureEnvelope()
    {
        string envelope = CommandDispatcher.Dispatch("no.such.command", Empty);
        using var doc = JsonDocument.Parse(envelope);

        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("no.such.command", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void PermissionsStatus_ReturnsOkWithElevatedField()
    {
        string envelope = CommandDispatcher.Dispatch("permissions.status", Empty);
        using var doc = JsonDocument.Parse(envelope);

        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        _ = doc.RootElement.GetProperty("data").GetProperty("elevated").GetBoolean(); // must not throw
    }

    [Fact]
    public void FeedbackCreateListGetDelete_RoundTripThroughDispatch()
    {
        using var createEnvelope = JsonDocument.Parse(CommandDispatcher.Dispatch(
            "feedback.create", Params("""{"category":"recommendation","title":"dispatch round trip","body":"b"}""")));
        Assert.True(createEnvelope.RootElement.GetProperty("ok").GetBoolean());
        int id = createEnvelope.RootElement.GetProperty("data").GetProperty("id").GetInt32();

        try
        {
            using var listEnvelope = JsonDocument.Parse(CommandDispatcher.Dispatch("feedback.list", Empty));
            Assert.Contains(listEnvelope.RootElement.GetProperty("data").EnumerateArray(), e => e.GetProperty("id").GetInt32() == id);

            using var getEnvelope = JsonDocument.Parse(CommandDispatcher.Dispatch("feedback.get", Params($$"""{"id":{{id}}}""")));
            Assert.Equal("dispatch round trip", getEnvelope.RootElement.GetProperty("data").GetProperty("title").GetString());
        }
        finally
        {
            using var deleteEnvelope = JsonDocument.Parse(CommandDispatcher.Dispatch("feedback.delete", Params($$"""{"id":{{id}}}""")));
            Assert.True(deleteEnvelope.RootElement.GetProperty("ok").GetBoolean());
        }
    }

    [Fact]
    public void FeedbackGet_UnknownId_ReturnsFailureEnvelope()
    {
        using var envelope = JsonDocument.Parse(CommandDispatcher.Dispatch("feedback.get", Params("""{"id":2147483647}""")));
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void FeedbackCheckDuplicates_UnreachableRepo_DegradesToNotChecked()
    {
        using var createEnvelope = JsonDocument.Parse(CommandDispatcher.Dispatch(
            "feedback.create", Params("""{"category":"issue","title":"unreachable repo check","body":"b"}""")));
        int id = createEnvelope.RootElement.GetProperty("data").GetProperty("id").GetInt32();

        try
        {
            using var envelope = JsonDocument.Parse(CommandDispatcher.Dispatch(
                "feedback.checkDuplicates", Params($$"""{"id":{{id}},"repo":"this-owner-does-not-exist-xyz/this-repo-does-not-exist-abc"}""")));

            Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
            Assert.False(envelope.RootElement.GetProperty("data").GetProperty("checked").GetBoolean());
            Assert.Empty(envelope.RootElement.GetProperty("data").GetProperty("duplicates").EnumerateArray());
        }
        finally
        {
            CommandDispatcher.Dispatch("feedback.delete", Params($$"""{"id":{{id}}}"""));
        }
    }
}

[Collection("Notepad app")]
public class CommandDispatcherNotepadTests(NotepadFixture notepad) : IDisposable
{
    private static JsonElement Params(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public void Dispose() => CommandDispatcher.Dispatch("focus.release", Params("{}"));

    [Fact]
    public void WindowsList_FilteredByApp_TagsEachWindowWithADisplayId()
    {
        string envelope = CommandDispatcher.Dispatch("windows.list", Params("""{"app":"notepad"}"""));
        using var doc = JsonDocument.Parse(envelope);

        var windows = doc.RootElement.GetProperty("data").GetProperty("windows");
        Assert.True(windows.GetArrayLength() > 0);
        foreach (var window in windows.EnumerateArray())
            Assert.Equal(JsonValueKind.Number, window.GetProperty("displayId").ValueKind);
    }

    [Fact]
    public void FocusHoldReleaseStatus_RoundTripThroughDispatch()
    {
        using var holdEnvelope = JsonDocument.Parse(
            CommandDispatcher.Dispatch("focus.hold", Params($$"""{"window":{{notepad.WindowId}}}""")));
        Assert.True(holdEnvelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(holdEnvelope.RootElement.GetProperty("data").GetProperty("held").GetBoolean());

        using var statusEnvelope = JsonDocument.Parse(CommandDispatcher.Dispatch("focus.status", Params("{}")));
        Assert.True(statusEnvelope.RootElement.GetProperty("data").GetProperty("held").GetBoolean());

        using var releaseEnvelope = JsonDocument.Parse(CommandDispatcher.Dispatch("focus.release", Params("{}")));
        Assert.False(releaseEnvelope.RootElement.GetProperty("data").GetProperty("held").GetBoolean());
    }
}
