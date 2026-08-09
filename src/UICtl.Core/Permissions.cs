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

        return new PermissionsStatus(elevated, targetElevated);
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
