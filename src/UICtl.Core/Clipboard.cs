using System.Runtime.InteropServices;
using UICtl.Core.Interop;

namespace UICtl.Core;

public static class Clipboard
{
    private const int OpenRetries = 5;
    private const int RetryDelayMs = 50;

    public static string? Get()
    {
        return WithClipboard(() =>
        {
            IntPtr handle = NativeMethods.GetClipboardData(Consts.CF_UNICODETEXT);
            if (handle == IntPtr.Zero) return null;

            IntPtr ptr = NativeMethods.GlobalLock(handle);
            if (ptr == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        });
    }

    public static void Set(string text)
    {
        WithClipboard<object?>(() =>
        {
            NativeMethods.EmptyClipboard();

            IntPtr hGlobal = NativeMethods.GlobalAlloc(Consts.GMEM_MOVEABLE, (nuint)((text.Length + 1) * sizeof(char)));
            if (hGlobal == IntPtr.Zero)
                throw new UiCtlException("GlobalAlloc failed");

            IntPtr target = NativeMethods.GlobalLock(hGlobal);
            if (target == IntPtr.Zero)
            {
                NativeMethods.GlobalFree(hGlobal);
                throw new UiCtlException("GlobalLock failed");
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                Marshal.WriteInt16(target, text.Length * sizeof(char), 0); // null terminator
            }
            finally
            {
                NativeMethods.GlobalUnlock(hGlobal);
            }

            // SetClipboardData transfers ownership of hGlobal to the OS on success;
            // on failure we still own it and must free it ourselves.
            if (NativeMethods.SetClipboardData(Consts.CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
            {
                NativeMethods.GlobalFree(hGlobal);
                throw new UiCtlException("SetClipboardData failed");
            }
            return null;
        });
    }

    private static T WithClipboard<T>(Func<T> action)
    {
        for (int attempt = 1; attempt <= OpenRetries; attempt++)
        {
            if (NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    return action();
                }
                finally
                {
                    NativeMethods.CloseClipboard();
                }
            }
            Thread.Sleep(RetryDelayMs);
        }
        throw new UiCtlException("could not open the clipboard (held by another process)");
    }
}
