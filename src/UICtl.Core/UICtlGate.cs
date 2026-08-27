namespace UICtl.Core;

/// <summary>
/// Thread-safe mirror of the enable/disable switch in the Activity Log
/// window. CommandDispatcher.Dispatch runs on the daemon's background accept
/// loop thread and must never touch the WPF window directly, so the log
/// window pushes state changes here (on the UI thread) and dispatch only
/// ever reads the plain values cached below. Mirrors macOS's UICtlGate.swift.
///
/// While the window is closed (or has never been opened), commands are
/// always enabled regardless of the toggle's remembered position - the
/// toggle only takes effect while the window is visibly open, so the state
/// can always be inspected by whoever is at the machine.
/// </summary>
public static class UICtlGate
{
    private static readonly object Lock = new();
    private static bool _windowOpen;
    private static bool _toggleEnabled = true;

    public static bool CommandsEnabled
    {
        get { lock (Lock) return !_windowOpen || _toggleEnabled; }
    }

    public static void SetWindowOpen(bool open)
    {
        lock (Lock) _windowOpen = open;
    }

    public static void SetToggleEnabled(bool enabled)
    {
        lock (Lock) _toggleEnabled = enabled;
    }
}
