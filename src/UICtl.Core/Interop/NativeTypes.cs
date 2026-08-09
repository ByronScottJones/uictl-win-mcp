using System.Runtime.InteropServices;

namespace UICtl.Core.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TOKEN_ELEVATION
{
    public int TokenIsElevated;
}

internal static class Consts
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_APPWINDOW = 0x00040000;

    public const int SW_RESTORE = 9;

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint TOKEN_QUERY = 0x0008;

    /// <summary>TOKEN_INFORMATION_CLASS.TokenElevation.</summary>
    public const int TokenElevation = 20;
}
