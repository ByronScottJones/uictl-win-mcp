using UICtl.Core.Interop;

namespace UICtl.Core;

public static class PixelSampler
{
    private const uint ClrInvalid = 0xFFFFFFFF;

    public static PixelColor ColorAt(Point at)
    {
        IntPtr hdc = NativeMethods.GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            throw new UiCtlException("GetDC failed");

        try
        {
            uint colorRef = NativeMethods.GetPixel(hdc, (int)Math.Round(at.X), (int)Math.Round(at.Y));
            if (colorRef == ClrInvalid)
                throw new UiCtlException($"could not sample pixel at ({at.X}, {at.Y})");

            // COLORREF is 0x00BBGGRR.
            byte r = (byte)(colorRef & 0xFF);
            byte g = (byte)((colorRef >> 8) & 0xFF);
            byte b = (byte)((colorRef >> 16) & 0xFF);
            return new PixelColor(r, g, b, 255);
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
        }
    }
}
