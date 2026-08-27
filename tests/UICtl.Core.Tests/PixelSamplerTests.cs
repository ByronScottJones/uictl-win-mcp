using UICtl.Core;

namespace UICtl.Core.Tests;

public class PixelSamplerTests
{
    [Fact]
    public void ColorAt_OnScreenPoint_ReturnsAnOpaqueColor()
    {
        var color = PixelSampler.ColorAt(new Point(0, 0));
        Assert.Equal(255, color.A);
    }

    [Fact]
    public void ColorAt_FarOffScreenPoint_Throws()
    {
        // GetPixel returns CLR_INVALID for a point outside every device context's
        // clipping region - large enough to be off any real desktop.
        Assert.Throws<UiCtlException>(() => PixelSampler.ColorAt(new Point(1_000_000, 1_000_000)));
    }
}
