using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace UICtl.Core;

/// <summary>
/// Windows.Media.Ocr wrapper. Unlike Vision on macOS, this API reports no
/// per-result confidence score at all - TextBlock.Confidence is always null
/// here. See MCP_INTERFACE.md's uictl_ocr section for why that's a genuine
/// platform limitation, not an oversight.
/// </summary>
public static class OCR
{
    public static IReadOnlyList<TextBlock> RecognizeImageFile(string path, Frame? region)
    {
        using var bitmap = new Bitmap(path);
        return Recognize(bitmap, new Point(0, 0), region);
    }

    public static IReadOnlyList<TextBlock> RecognizeCapture(Capture capture, Frame? region) =>
        Recognize(capture.Image, capture.Origin, region);

    private static IReadOnlyList<TextBlock> Recognize(Bitmap image, Point imageOrigin, Frame? region)
    {
        Bitmap? cropped = null;
        try
        {
            Bitmap source = image;
            Point origin = imageOrigin;

            if (region is { } r)
            {
                var localRect = Rectangle.Intersect(
                    new Rectangle((int)Math.Round(r.X - imageOrigin.X), (int)Math.Round(r.Y - imageOrigin.Y), (int)Math.Round(r.W), (int)Math.Round(r.H)),
                    new Rectangle(0, 0, image.Width, image.Height));
                if (localRect.Width <= 0 || localRect.Height <= 0)
                    return [];

                cropped = image.Clone(localRect, image.PixelFormat);
                source = cropped;
                origin = new Point(imageOrigin.X + localRect.X, imageOrigin.Y + localRect.Y);
            }

            using var softwareBitmap = RunSync(ToSoftwareBitmapAsync(source));
            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                ?? throw new UiCtlException("no OCR language pack is installed for the current user profile");
            var result = RunSync(engine.RecognizeAsync(softwareBitmap).AsTask());

            var blocks = new List<TextBlock>();
            foreach (var line in result.Lines)
            {
                if (line.Words.Count == 0) continue;
                blocks.Add(new TextBlock(line.Text, UnionFrame(line.Words, origin), Confidence: null));
            }
            return blocks;
        }
        finally
        {
            cropped?.Dispose();
        }
    }

    private static Frame UnionFrame(IReadOnlyList<OcrWord> words, Point origin)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxRight = double.MinValue, maxBottom = double.MinValue;
        foreach (var word in words)
        {
            var r = word.BoundingRect;
            minX = Math.Min(minX, r.X);
            minY = Math.Min(minY, r.Y);
            maxRight = Math.Max(maxRight, r.X + r.Width);
            maxBottom = Math.Max(maxBottom, r.Y + r.Height);
        }
        return new Frame(origin.X + minX, origin.Y + minY, maxRight - minX, maxBottom - minY);
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(Bitmap bitmap)
    {
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Png);
        memoryStream.Position = 0;

        using var randomAccessStream = memoryStream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        return await decoder.GetSoftwareBitmapAsync();
    }

    // No UI/ASP.NET SynchronizationContext exists in this console/daemon process,
    // so blocking on the task here cannot deadlock the way it can in those hosts.
    private static T RunSync<T>(Task<T> task) => task.GetAwaiter().GetResult();
}
