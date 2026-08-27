using UICtl.Core;

namespace UICtl.Core.Tests;

/// <summary>
/// UICtlGate is process-wide static state that CommandDispatcher.Dispatch
/// now reads on every call (see Phase 4), so every test resets it back to
/// the default (window never opened, toggle enabled) in Dispose - a test
/// left in the "disabled" state would otherwise make every other test's
/// Dispatch call fail with "commands are disabled". Joins the "Notepad app"
/// collection (see CommandDispatcherTests) so xunit never runs this
/// alongside anything that calls Dispatch.
/// </summary>
[Collection("Notepad app")]
public class UICtlGateTests : IDisposable
{
    public void Dispose()
    {
        UICtlGate.SetWindowOpen(false);
        UICtlGate.SetToggleEnabled(true);
    }

    [Fact]
    public void CommandsEnabled_DefaultsTrue_WindowNeverOpened()
    {
        Assert.True(UICtlGate.CommandsEnabled);
    }

    [Fact]
    public void CommandsEnabled_TogglingWithoutOpeningTheWindow_HasNoEffect()
    {
        UICtlGate.SetToggleEnabled(false);
        Assert.True(UICtlGate.CommandsEnabled);
    }

    [Fact]
    public void CommandsEnabled_WindowOpenAndToggleOff_IsFalse()
    {
        UICtlGate.SetWindowOpen(true);
        UICtlGate.SetToggleEnabled(false);
        Assert.False(UICtlGate.CommandsEnabled);
    }

    [Fact]
    public void CommandsEnabled_ClosingTheWindowReenablesRegardlessOfToggle()
    {
        UICtlGate.SetWindowOpen(true);
        UICtlGate.SetToggleEnabled(false);
        Assert.False(UICtlGate.CommandsEnabled);

        UICtlGate.SetWindowOpen(false);
        Assert.True(UICtlGate.CommandsEnabled);
    }
}
