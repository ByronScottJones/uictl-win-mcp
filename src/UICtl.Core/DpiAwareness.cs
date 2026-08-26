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
    public static void EnsurePerMonitorAware()
    {
        NativeMethods.SetProcessDpiAwarenessContext(Consts.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }
}
