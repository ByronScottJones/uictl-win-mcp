using UICtl.Core;

namespace UICtl.Core.Tests;

/// <summary>
/// Round-trips the real system clipboard - not a mock. Save/restore whatever
/// was there before so running this suite doesn't clobber the developer's
/// actual clipboard contents. In the "Notepad app" collection (rather than
/// its own, default one) so it never runs in parallel with
/// InputSynthesisNotepadTests, which also touches the real clipboard.
/// </summary>
[Collection("Notepad app")]
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
        // Restore exactly what was there before, even if that was "no text" -
        // leaving this test's marker behind would be its own kind of clobbering.
        Clipboard.Set(_original ?? "");
    }
}
