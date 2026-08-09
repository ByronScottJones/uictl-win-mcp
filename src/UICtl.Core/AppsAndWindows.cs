using System.Diagnostics;
using System.Text;
using UICtl.Core.Interop;

namespace UICtl.Core;

public static class AppsAndWindows
{
    public static IReadOnlyList<AppInfo> ListApps(bool includeBackground)
    {
        var regularPids = new HashSet<int>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (IsRegularTopLevelWindow(hWnd))
            {
                NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
                regularPids.Add((int)pid);
            }
            return true;
        }, IntPtr.Zero);

        IEnumerable<int> pids = regularPids;
        if (includeBackground)
        {
            using var allProcesses = new DisposableProcessList(Process.GetProcesses());
            pids = regularPids.Union(allProcesses.Processes.Select(p => p.Id)).ToList();
        }

        var apps = new List<AppInfo>();
        foreach (var pid in pids)
        {
            using var process = TryGetProcessById(pid);
            if (process is null) continue;
            apps.Add(new AppInfo(pid, TryGetProcessName(process), AppSelector.GetPackageFamilyName(process)));
        }
        return apps;
    }

    public static IReadOnlyList<WindowInfo> ListWindows(int? pidFilter)
    {
        var windows = new List<WindowInfo>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            if (pidFilter is { } filter && (int)pid != filter) return true;

            int length = NativeMethods.GetWindowTextLengthW(hWnd);
            if (length == 0) return true; // skip untitled helper/owned windows

            var buffer = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(hWnd, buffer, buffer.Capacity);

            if (!NativeMethods.GetWindowRect(hWnd, out var rect)) return true;

            windows.Add(new WindowInfo(
                hWnd.ToInt64(),
                (int)pid,
                buffer.ToString(),
                new Frame(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)));
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    public static AppInfo Activate(string appSelector, long? windowId)
    {
        int pid = AppSelector.Resolve(appSelector);
        IntPtr target = windowId is { } id ? new IntPtr(id) : FindMainWindow(pid);
        if (target == IntPtr.Zero)
            throw new UiCtlException($"no window found for \"{appSelector}\"");

        BringToFront(target);

        using var process = Process.GetProcessById(pid);
        return new AppInfo(pid, TryGetProcessName(process), AppSelector.GetPackageFamilyName(process));
    }

    internal static IntPtr FindMainWindow(int pid)
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!IsRegularTopLevelWindow(hWnd)) return true;
            NativeMethods.GetWindowThreadProcessId(hWnd, out var windowPid);
            if ((int)windowPid != pid) return true;
            found = hWnd;
            return false; // stop at the first match
        }, IntPtr.Zero);
        return found;
    }

    private static bool IsRegularTopLevelWindow(IntPtr hWnd)
    {
        if (!NativeMethods.IsWindowVisible(hWnd)) return false;
        if (NativeMethods.GetWindowTextLengthW(hWnd) == 0) return false;
        int exStyle = NativeMethods.GetWindowLong(hWnd, Consts.GWL_EXSTYLE);
        return (exStyle & Consts.WS_EX_TOOLWINDOW) == 0;
    }

    /// <summary>
    /// SetForegroundWindow alone is routinely denied by Windows' foreground-lock
    /// heuristic when the caller isn't already the foreground process. Attaching
    /// input queues with the current foreground thread is the standard workaround.
    /// </summary>
    private static void BringToFront(IntPtr hWnd)
    {
        if (!NativeMethods.IsWindow(hWnd))
            throw new UiCtlException($"window {hWnd} no longer exists");

        if (NativeMethods.IsIconic(hWnd))
            NativeMethods.ShowWindow(hWnd, Consts.SW_RESTORE);

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        uint currentThread = NativeMethods.GetCurrentThreadId();
        uint foregroundThread = foreground == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint targetThread = NativeMethods.GetWindowThreadProcessId(hWnd, out _);

        bool attachedToForeground = foregroundThread != 0 && foregroundThread != currentThread
            && NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        bool attachedToTarget = targetThread != currentThread
            && NativeMethods.AttachThreadInput(currentThread, targetThread, true);

        try
        {
            NativeMethods.SetForegroundWindow(hWnd);
            NativeMethods.BringWindowToTop(hWnd);
        }
        finally
        {
            if (attachedToTarget) NativeMethods.AttachThreadInput(currentThread, targetThread, false);
            if (attachedToForeground) NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private static Process? TryGetProcessById(int pid)
    {
        try { return Process.GetProcessById(pid); }
        catch (ArgumentException) { return null; }
    }

    private static string TryGetProcessName(Process process)
    {
        try { return process.ProcessName; }
        catch { return ""; }
    }

    private sealed class DisposableProcessList : IDisposable
    {
        public Process[] Processes { get; }
        public DisposableProcessList(Process[] processes) => Processes = processes;
        public void Dispose()
        {
            foreach (var p in Processes) p.Dispose();
        }
    }
}
