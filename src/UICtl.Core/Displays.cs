using UICtl.Core.Interop;

namespace UICtl.Core;

/// <summary>
/// Enumerates monitors via EnumDisplayMonitors, mirroring macOS's
/// Displays.swift: an index that matches what `screenshot --screen &lt;index&gt;`
/// expects, the monitor handle as displayId, bounds, isMain, and a
/// DPI-derived scale factor in place of macOS's pointPixelScale. See
/// ENGINEERING.md's "Coordinate spaces" / "Multiple displays" sections.
/// </summary>
public static class Displays
{
    public static IReadOnlyList<DisplayInfo> List()
    {
        var handles = new List<IntPtr>();
        bool ok = NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            handles.Add(hMonitor);
            return true;
        }, IntPtr.Zero);
        if (!ok)
            throw new UiCtlException("EnumDisplayMonitors failed");

        var result = new List<DisplayInfo>(handles.Count);
        for (int i = 0; i < handles.Count; i++)
            result.Add(Describe(i, handles[i]));
        return result;
    }

    private static DisplayInfo Describe(int index, IntPtr hMonitor)
    {
        var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfoW(hMonitor, ref info))
            throw new UiCtlException($"GetMonitorInfoW failed for monitor {hMonitor}");

        var rect = info.rcMonitor;
        var frame = new Frame(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        bool isMain = (info.dwFlags & Consts.MONITORINFOF_PRIMARY) != 0;

        double scale = 1.0;
        if (NativeMethods.GetDpiForMonitor(hMonitor, Consts.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0)
            scale = dpiX / 96.0;

        return new DisplayInfo(index, hMonitor.ToInt64(), frame, isMain, scale);
    }
}
