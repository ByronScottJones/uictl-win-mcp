using UICtl.Core;
using UICtl.Core.Tests.TestSupport;

namespace UICtl.Core.Tests;

/// <summary>
/// Exercises the real Win32 foreground-window APIs against a real Notepad
/// window. EnsureFocused_ReclaimsForegroundFromADifferentWindow covers the
/// general "reclaim foreground from whatever else is now frontmost" path
/// end to end via a real HWND comparison - the same comparison the
/// FocusHold.Reactivate follow-up fix relies on. It doesn't reproduce the
/// exact scenario that PR #3's review originally flagged (a second window
/// belonging to the *same* process being mistaken for the held one),
/// because this machine's Notepad is single-instance (see
/// [[uictl-dev-machine]]) and so can't produce two windows under one pid to
/// test against - the second window here (Paint) is a different process.
/// </summary>
[Collection("Notepad app")]
public class FocusHoldTests(NotepadFixture notepad) : IDisposable
{
    public void Dispose() => FocusHold.Release(); // never leave a hold dangling into the next test

    [Fact]
    public void Hold_ThenStatus_ReportsHeldWithTheResolvedWindow()
    {
        var status = FocusHold.Hold(notepad.WindowId, null);

        Assert.Equal(true, status["held"]);
        Assert.Equal(notepad.WindowId, status["windowId"]);
        Assert.Equal(notepad.Pid, status["pid"]);
    }

    [Fact]
    public void Release_ClearsTheHold()
    {
        FocusHold.Hold(notepad.WindowId, null);

        FocusHold.Release();

        Assert.Equal(false, FocusHold.Status()["held"]);
    }

    [Fact]
    public void EnsureFocused_WithNothingHeld_ReturnsNotHeld()
    {
        FocusHold.Release();

        Assert.Equal("notHeld", FocusHold.EnsureFocused());
    }

    [Fact]
    public void EnsureFocused_ReclaimsForegroundFromADifferentWindow()
    {
        AppsAndWindows.BringToFront(new IntPtr(notepad.WindowId));
        FocusHold.Hold(notepad.WindowId, null);

        var (otherPid, otherWindowId) = NotepadFixture.LaunchAnotherApp();
        try
        {
            AppsAndWindows.BringToFront(new IntPtr(otherWindowId));

            string result = FocusHold.EnsureFocused();

            Assert.Equal("reactivated", result);
        }
        finally
        {
            NotepadFixture.Kill(otherPid);
        }
    }
}
