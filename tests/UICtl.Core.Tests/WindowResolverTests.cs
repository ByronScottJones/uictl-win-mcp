using UICtl.Core;
using UICtl.Core.Tests.TestSupport;

namespace UICtl.Core.Tests;

public class WindowResolverTests
{
    [Fact]
    public void Resolve_NeitherWindowIdNorAppSelector_Throws()
    {
        Assert.Throws<UiCtlException>(() => WindowResolver.Resolve(null, null));
    }

    [Fact]
    public void Resolve_NonExistentWindowId_Throws()
    {
        // Unlikely to ever be a live HWND value.
        Assert.Throws<UiCtlException>(() => WindowResolver.Resolve(0x7FFFFFF0, null));
    }
}

[Collection("Notepad app")]
public class WindowResolverNotepadTests(NotepadFixture notepad)
{
    [Fact]
    public void Resolve_ByWindowId_ReturnsItsOwningPid()
    {
        var resolved = WindowResolver.Resolve(notepad.WindowId, null);
        Assert.Equal(notepad.WindowId, resolved.WindowId);
        Assert.Equal(notepad.Pid, resolved.Pid);
    }

    [Fact]
    public void Resolve_ByAppSelector_FindsTheSameWindowAsFindMainWindow()
    {
        var resolved = WindowResolver.Resolve(null, "notepad");
        Assert.Equal(notepad.Pid, resolved.Pid);
        Assert.Equal(AppsAndWindows.FindMainWindow(notepad.Pid).ToInt64(), resolved.WindowId);
    }
}
