using System.Drawing;
using System.Drawing.Imaging;
using UICtl.Core.Interop;

namespace UICtl.Core;

/// <summary>A captured bitmap plus the virtual-screen point its top-left corner corresponds to.</summary>
public sealed class Capture : IDisposable
{
    public required Bitmap Image { get; init; }
    public required Point Origin { get; init; }

    public void Dispose() => Image.Dispose();
}

public static class ScreenCapture
{
    /// <summary>PW_RENDERFULLCONTENT: captures DWM-composited/GPU-rendered content that a plain BitBlt of the window's own DC misses on modern apps.</summary>
    private const uint PW_RENDERFULLCONTENT = 0x2;

    public static Capture CaptureWindow(long windowId)
    {
        var hWnd = new IntPtr(windowId);
        if (!NativeMethods.IsWindow(hWnd))
            throw new UiCtlException($"window {windowId} does not exist");
        if (!NativeMethods.GetWindowRect(hWnd, out var rect))
            throw new UiCtlException($"GetWindowRect failed for window {windowId}");

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            throw new UiCtlException($"window {windowId} has an empty frame");

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            IntPtr hdc = graphics.GetHdc();
            try
            {
                if (!NativeMethods.PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT))
                    throw new UiCtlException($"PrintWindow failed for window {windowId}");
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        return new Capture { Image = bitmap, Origin = new Point(rect.Left, rect.Top) };
    }

    public static Capture CaptureDisplay(int index)
    {
        var monitors = EnumerateMonitorRects();
        if (index < 0 || index >= monitors.Count)
            throw new UiCtlException($"no display at index {index} ({monitors.Count} available)");

        var rect = monitors[index];
        var bitmap = new Bitmap((int)rect.W, (int)rect.H, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen((int)rect.X, (int)rect.Y, 0, 0, new System.Drawing.Size((int)rect.W, (int)rect.H));
        }

        return new Capture { Image = bitmap, Origin = new Point((int)rect.X, (int)rect.Y) };
    }

    /// <summary>Draws numbered boxes over each element's frame directly on <paramref name="image"/> and returns the number-to-element legend.</summary>
    public static IReadOnlyList<AnnotatedElement> Annotate(Bitmap image, Point captureOrigin, IReadOnlyList<ElementInfo> elements)
    {
        using var graphics = Graphics.FromImage(image);
        using var pen = new Pen(Color.Red, 2);
        using var font = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        using var labelBackground = new SolidBrush(Color.Red);

        var annotated = new List<AnnotatedElement>(elements.Count);
        for (int i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            int number = i + 1;

            float x = (float)(element.Frame.X - captureOrigin.X);
            float y = (float)(element.Frame.Y - captureOrigin.Y);
            float w = (float)element.Frame.W;
            float h = (float)element.Frame.H;
            graphics.DrawRectangle(pen, x, y, w, h);

            string label = number.ToString();
            var labelSize = graphics.MeasureString(label, font);
            float labelY = Math.Max(0, y - labelSize.Height);
            graphics.FillRectangle(labelBackground, x, labelY, labelSize.Width, labelSize.Height);
            graphics.DrawString(label, font, textBrush, x, labelY);

            annotated.Add(new AnnotatedElement(number, element));
        }
        return annotated;
    }

    public static void SavePng(Bitmap image, string path)
    {
        string? dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            System.IO.Directory.CreateDirectory(dir);
        image.Save(path, ImageFormat.Png);
    }

    private static List<Frame> EnumerateMonitorRects()
    {
        var rects = new List<Frame>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr _, IntPtr _, ref RECT lprcMonitor, IntPtr _) =>
        {
            rects.Add(new Frame(lprcMonitor.Left, lprcMonitor.Top, lprcMonitor.Right - lprcMonitor.Left, lprcMonitor.Bottom - lprcMonitor.Top));
            return true;
        }, IntPtr.Zero);
        return rects;
    }
}
