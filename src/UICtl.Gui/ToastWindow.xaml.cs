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
        PositionNearCursor();

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        Show();

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    /// <summary>
    /// Positions just above and to the right of the cursor, clamped to the
    /// bounds of whichever display the cursor is actually on. Window.Left/Top
    /// are DIPs scaled by that same display's DPI here, which is correct on
    /// this machine's single display; a genuinely mixed-DPI multi-monitor rig
    /// would need each monitor's own scale applied per-axis-segment when the
    /// window straddles a boundary, which isn't handled (and wasn't
    /// verifiable on this single-monitor dev machine).
    /// </summary>
    private void PositionNearCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return;

        var displays = Displays.List();
        var containing = displays.FirstOrDefault(d =>
            cursor.X >= d.Frame.X && cursor.X < d.Frame.X + d.Frame.W &&
            cursor.Y >= d.Frame.Y && cursor.Y < d.Frame.Y + d.Frame.H)
            ?? displays.FirstOrDefault(d => d.IsMain);
        double scale = containing?.Scale ?? 1.0;

        double left = cursor.X / scale + 16;
        double top = cursor.Y / scale + 16;

        if (containing is not null)
        {
            double minLeft = containing.Frame.X / scale;
            double minTop = containing.Frame.Y / scale;
            double maxLeft = (containing.Frame.X + containing.Frame.W) / scale - ActualWidth;
            double maxTop = (containing.Frame.Y + containing.Frame.H) / scale - ActualHeight;
            left = Math.Clamp(left, minLeft, Math.Max(minLeft, maxLeft));
            top = Math.Clamp(top, minTop, Math.Max(minTop, maxTop));
        }

        Left = left;
        Top = top;
    }

    private void FadeOut()
    {
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
        animation.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, animation);
    }
}
