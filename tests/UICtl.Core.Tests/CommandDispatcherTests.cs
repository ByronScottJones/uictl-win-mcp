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
