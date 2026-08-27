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
        // 0 is deterministically not a window (IsWindow(NULL) is always false),
        // unlike a large constant that could theoretically collide with a real HWND.
        Assert.Throws<UiCtlException>(() => WindowResolver.Resolve(0, null));
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
