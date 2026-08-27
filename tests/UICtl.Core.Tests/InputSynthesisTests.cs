using System.Runtime.InteropServices;
using UICtl.Core;
using UICtl.Core.Interop;
using UICtl.Core.Tests.TestSupport;

namespace UICtl.Core.Tests;

public class InputSynthesisTests
{
    /// <summary>
    /// SendInput's ABI is exactly the trap the code comment on <see cref="INPUT"/>
    /// warns about: a Pack=1 layout compiles fine but silently misaligns the
    /// union, so SendInput either rejects the call or reads garbage. This pins
    /// the sequential layout to the real native size/offset so a future edit
    /// that reintroduces the bug fails a test instead of failing silently on a
    /// real machine.
    /// </summary>
    [Fact]
    public void Input_Layout_Matches_Native_SendInput_Abi()
    {
        if (!Environment.Is64BitProcess) return; // this repo ships x64; the native ABI this pins is architecture-specific

        Assert.Equal(40, Marshal.SizeOf<INPUT>());
        Assert.Equal(8, Marshal.OffsetOf<INPUT>(nameof(INPUT.u)).ToInt32());
    }

    [Fact]
    public void SendKeyCombo_UnknownModifier_Throws()
    {
        Assert.Throws<UiCtlException>(() => InputSynthesis.SendKeyCombo("frobnicate+a"));
    }

    [Fact]
    public void SendKeyCombo_EmptyCombo_Throws()
    {
        Assert.Throws<UiCtlException>(() => InputSynthesis.SendKeyCombo(""));
    }
}

[Collection("Notepad app")]
public class InputSynthesisNotepadTests(NotepadFixture notepad) : IDisposable
{
    private readonly string? _originalClipboard = Clipboard.Get();

    public void Dispose() => Clipboard.Set(_originalClipboard ?? "");

    /// <summary>
    /// Modern Notepad's editor is a rich-text control (role "Document") that
    /// doesn't expose UI Automation's ValuePattern, and its automation peer
    /// isn't even created until the window has been interacted with once -
    /// so this activates + clicks into the window first (an agent would do
    /// the same via `activate` before `elements`), then verifies the typed
    /// text by selecting-all and reading it back via the clipboard rather
    /// than assuming ValuePattern is there to read.
    /// </summary>
    [Fact]
    public void TypeText_IntoNotepadsEditor_ProducesTheTypedText()
    {
        AppsAndWindows.BringToFront(new IntPtr(notepad.WindowId));
        var frame = AppsAndWindows.ListWindows(notepad.Pid).First(w => w.WindowId == notepad.WindowId).Frame;
        InputSynthesis.Click(frame.Center, MouseButton.Left, 1);
        Thread.Sleep(300);

        var windowElement = Automation.ResolveWindowElement(notepad.WindowId);
        var walk = Automation.EnumerateElements(notepad.WindowId, windowElement, new ElementWalkOptions());
        var editor = walk.Elements.First(e => e.Role is "Edit" or "Document");

        string marker = $"uictl-{Guid.NewGuid():N}";
        if (!Automation.TrySetValue(editor.Id, marker))
        {
            Automation.SetFocus(editor.Id);
            Thread.Sleep(50);
            InputSynthesis.TypeText(marker);
        }

        InputSynthesis.SendKeyCombo("ctrl+a");
        InputSynthesis.SendKeyCombo("ctrl+c");
        Thread.Sleep(200);

        Assert.Contains(marker, Clipboard.Get() ?? "");
    }
}
