using System.Runtime.InteropServices;
using UICtl.Core.Interop;

namespace UICtl.Core;

/// <summary>
/// Must be called once, as early as possible, in any process that will call
/// GetWindowRect/UI Automation/SendInput - otherwise the OS silently scales
/// every coordinate to the primary monitor's DPI instead of the real physical
/// pixel on whichever monitor a window is actually on. See ENGINEERING.md's
/// "Coordinate spaces" section.
/// </summary>
public static class DpiAwareness
{
    private static bool _attempted;

    /// <summary>
    /// Idempotent: SetProcessDpiAwarenessContext can only succeed once per
    /// process (a second call always fails with E_ACCESSDENIED), and
    /// `daemon start --foreground` runs both Program.cs's and
    /// DaemonServer.RunForegroundAsync's call sites in the same process - a
    /// bare unconditional call would silently fail on whichever runs second.
    /// </summary>
    public static void EnsurePerMonitorAware()
    {
        if (_attempted) return;
        _attempted = true;

        if (!NativeMethods.SetProcessDpiAwarenessContext(Consts.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
        {
            int error = Marshal.GetLastWin32Error();
            Console.Error.WriteLine(
                $"[{DateTime.UtcNow:O}] warning: SetProcessDpiAwarenessContext failed (Win32 error {error}) " +
                "- window/element coordinates may be wrong on non-100%-scaled displays.");
        }
    }
}
