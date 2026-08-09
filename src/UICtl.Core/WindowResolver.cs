using UICtl.Core.Interop;

namespace UICtl.Core;

public static class WindowResolver
{
    public static ResolvedWindow Resolve(long? windowId, string? appSelector)
    {
        if (windowId is { } id)
        {
            var hWnd = new IntPtr(id);
            if (!NativeMethods.IsWindow(hWnd))
                throw new UiCtlException($"window {id} does not exist");
            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            return new ResolvedWindow(id, (int)pid);
        }

        if (appSelector is { } selector)
        {
            int pid = AppSelector.Resolve(selector);
            IntPtr hWnd = AppsAndWindows.FindMainWindow(pid);
            if (hWnd == IntPtr.Zero)
                throw new UiCtlException($"no window found for \"{selector}\"");
            return new ResolvedWindow(hWnd.ToInt64(), pid);
        }

        throw new UiCtlException("either a window id or an app selector is required");
    }
}
