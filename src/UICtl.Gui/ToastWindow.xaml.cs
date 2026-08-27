using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UICtl.Core;
using UICtl.Core.Interop;

namespace UICtl.Gui;

/// <summary>
/// A brief, non-interactive on-screen reminder that uictl is actively driving
/// the UI, shown once per call. Rapid successive calls coalesce into the same
/// panel (its text and fade timer just reset) rather than stacking up toasts.
/// Reused across the daemon's lifetime rather than recreated. Mirrors macOS's
/// GUI/ToastController.swift.
/// </summary>
public partial class ToastWindow : Window
{
    private static ToastWindow? _instance;
    public static ToastWindow Instance => _instance ??= new ToastWindow();

    private readonly DispatcherTimer _hideTimer;

    private ToastWindow()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            FadeOut();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Click-through, never steals activation, and hidden from Alt-Tab -
        // this is a transient reminder, never meant to take input focus away
        // from whatever the automation is actually driving.
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = NativeMethods.GetWindowLong(hwnd, Consts.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, Consts.GWL_EXSTYLE, exStyle | Consts.WS_EX_TRANSPARENT | Consts.WS_EX_NOACTIVATE | Consts.WS_EX_TOOLWINDOW);
    }

    public void ShowFor(ActivityEntry entry)
    {
        MessageText.Text = $"{(entry.Ok ? "✓" : "✗")} uictl: {entry.Command}";
        UpdateLayout();
        // Force the HWND to exist (without making it visible yet) so
        // PositionNearCursor has a real window to call SetWindowPos on, even
        // on the very first call.
        new WindowInteropHelper(this).EnsureHandle();
        PositionNearCursor();

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        Show();

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    /// <summary>
    /// Positions just above and to the right of the cursor, clamped to the
    /// bounds of whichever display the cursor is actually on. Uses
    /// SetWindowPos with raw physical-pixel coordinates rather than WPF's own
    /// Window.Left/Top (DIPs, virtualized relative to the primary monitor's
    /// DPI) - the latter would place this wrong on a secondary monitor whose
    /// DPI differs from whatever's "between" it and the primary monitor.
    /// Residual limitation: the clamp's upper bound converts this window's
    /// own ActualWidth/Height (DIPs) to physical pixels using the target
    /// monitor's scale, but WPF may still have laid it out at a different
    /// monitor's DPI if this is the very first time it's ever been
    /// positioned - a narrow edge case affecting only how tightly the toast
    /// is pushed back from the screen edge, not gross mispositioning.
    /// </summary>
    private void PositionNearCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return;

        var displays = Displays.List();
        var containing = displays.FirstOrDefault(d =>
            cursor.X >= d.Frame.X && cursor.X < d.Frame.X + d.Frame.W &&
            cursor.Y >= d.Frame.Y && cursor.Y < d.Frame.Y + d.Frame.H)
            ?? displays.FirstOrDefault(d => d.IsMain);

        int targetX = cursor.X + 16;
        int targetY = cursor.Y + 16;

        if (containing is not null)
        {
            int widthPx = (int)Math.Round(ActualWidth * containing.Scale);
            int heightPx = (int)Math.Round(ActualHeight * containing.Scale);
            int minX = (int)containing.Frame.X;
            int minY = (int)containing.Frame.Y;
            int maxX = (int)(containing.Frame.X + containing.Frame.W) - widthPx;
            int maxY = (int)(containing.Frame.Y + containing.Frame.H) - heightPx;
            targetX = Math.Clamp(targetX, minX, Math.Max(minX, maxX));
            targetY = Math.Clamp(targetY, minY, Math.Max(minY, maxY));
        }

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, targetX, targetY, 0, 0,
            Consts.SWP_NOSIZE | Consts.SWP_NOZORDER | Consts.SWP_NOACTIVATE);
    }

    private void FadeOut()
    {
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
        animation.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, animation);
    }
}
