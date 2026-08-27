using UICtl.Core;

namespace UICtl.Core.Tests;

/// <summary>
/// Round-trips the real system clipboard - not a mock. Save/restore whatever
/// was there before so running this suite doesn't clobber the developer's
/// actual clipboard contents.
/// </summary>
public class ClipboardTests : IDisposable
{
    private readonly string? _original = Clipboard.Get();

    [Fact]
    public void SetThenGet_RoundTripsExactText()
    {
        string marker = $"uictl-test-{Guid.NewGuid():N}";
        Clipboard.Set(marker);
        Assert.Equal(marker, Clipboard.Get());
    }

    [Fact]
    public void Set_EmptyString_RoundTrips()
    {
        Clipboard.Set("");
        Assert.Equal("", Clipboard.Get());
    }

    public void Dispose()
    {
        if (_original is not null)
            Clipboard.Set(_original);
    }
}
