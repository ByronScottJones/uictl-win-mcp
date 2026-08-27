using UICtl.Core.Interop;

namespace UICtl.Core;

/// <summary>
/// Windows has no Accessibility/Screen-Recording consent prompt like macOS TCC.
/// The equivalent precondition is elevation parity: UI Automation and SendInput
/// are blocked by UIPI when the target process outranks uictl. See MCP_INTERFACE.md.
/// </summary>
public static class Permissions
{
    public static PermissionsStatus Status(string? appSelector)
    {
        bool elevated = IsElevated(NativeMethods.GetCurrentProcess());

        bool? targetElevated = null;
        if (appSelector is not null)
        {
            int pid = AppSelector.Resolve(appSelector);
            targetElevated = IsElevatedPid((uint)pid);
        }

        return new PermissionsStatus(elevated, targetElevated, IsInteractiveSession());
    }

    /// <summary>
    /// Whether this process's window station is attached to the visible,
    /// interactive desktop - false for a process started in a non-interactive
    /// session, which is exactly what happens if this daemon gets auto-spawned
    /// from a channel like a plain SSH command on Windows (sshd, by default,
    /// does not attach spawned processes to the interactively logged-in
    /// session's desktop). UI Automation, SendInput, and screen capture then
    /// silently fail or no-op instead of raising a clear error - this exists
    /// so callers (see UICtl.Mcp's SessionInteractivityCheck) can detect and
    /// surface that situation proactively rather than leave it as a mystery.
    /// </summary>
    private static bool IsInteractiveSession()
    {
        IntPtr winStation = NativeMethods.GetProcessWindowStation();
        if (winStation == IntPtr.Zero) return false;

        var flags = new USEROBJECTFLAGS();
        uint size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<USEROBJECTFLAGS>();
        if (!NativeMethods.GetUserObjectInformation(winStation, Consts.UOI_FLAGS, ref flags, size, out _))
            return false;

        return (flags.dwFlags & Consts.WSF_VISIBLE) != 0;
    }

    private static bool IsElevatedPid(uint pid)
    {
        IntPtr handle = NativeMethods.OpenProcess(Consts.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
            throw new UiCtlException($"could not open process {pid} to check elevation");
        try
        {
            return IsElevated(handle);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static bool IsElevated(IntPtr processHandle)
    {
        if (!NativeMethods.OpenProcessToken(processHandle, Consts.TOKEN_QUERY, out var token))
            throw new UiCtlException("OpenProcessToken failed");
        try
        {
            if (!NativeMethods.GetTokenInformation(token, Consts.TokenElevation, out var elevation, System.Runtime.InteropServices.Marshal.SizeOf<TOKEN_ELEVATION>(), out _))
                throw new UiCtlException("GetTokenInformation failed");
            return elevation.TokenIsElevated != 0;
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }
}
