using UICtl.Core;
using UICtl.Core.Tests.TestSupport;

namespace UICtl.Core.Tests;

[Collection("Notepad app")]
public class ElementStoreTests(NotepadFixture notepad)
{
    [Fact]
    public void Register_ThenLookup_ReturnsTheSameElement()
    {
        long windowId = notepad.WindowId;
        var windowElement = Automation.ResolveWindowElement(windowId);
        ElementStore.Reset(windowId);

        string id = ElementStore.Register(windowId, windowElement);

        Assert.Equal($"{windowId}-1", id);
        Assert.Same(windowElement, ElementStore.Lookup(id));
    }

    [Fact]
    public void Register_Twice_AssignsIncrementingIdsUnderTheSameWindow()
    {
        long windowId = notepad.WindowId;
        var windowElement = Automation.ResolveWindowElement(windowId);
        ElementStore.Reset(windowId);

        string first = ElementStore.Register(windowId, windowElement);
        string second = ElementStore.Register(windowId, windowElement);

        Assert.Equal($"{windowId}-1", first);
        Assert.Equal($"{windowId}-2", second);
    }

    [Fact]
    public void Lookup_UnknownId_ReturnsNull()
    {
        Assert.Null(ElementStore.Lookup("no-such-window-99"));
    }

    [Fact]
    public void Reset_ClearsOnlyTheGivenWindowsEntries()
    {
        long windowA = notepad.WindowId;
        long windowB = notepad.WindowId + 1; // synthetic - never resolved, just a distinct key
        var windowElement = Automation.ResolveWindowElement(notepad.WindowId);

        ElementStore.Reset(windowA);
        ElementStore.Reset(windowB);
        string idA = ElementStore.Register(windowA, windowElement);
        string idB = ElementStore.Register(windowB, windowElement);

        ElementStore.Reset(windowA);

        Assert.Null(ElementStore.Lookup(idA));
        Assert.Same(windowElement, ElementStore.Lookup(idB));
    }
}
