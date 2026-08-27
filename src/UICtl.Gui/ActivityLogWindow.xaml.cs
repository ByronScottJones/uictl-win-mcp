using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using UICtl.Core;

namespace UICtl.Gui;

/// <summary>
/// The on-screen window opened by `uictl log show`: a live "active/idle"
/// banner plus a scrollable grid of every call the daemon has handled, with
/// a button to export the log as JSON - the same summarized/truncated/
/// redacted params and response text shown in the grid, via
/// ActivityLog.ExportJson. Reused across repeated `log show` calls rather
/// than recreated. All methods must be called on the UI thread. Mirrors
/// macOS's GUI/ActivityWindowController.swift; WPF's DataGrid gives cell
/// select + Ctrl+C copy natively, so unlike macOS's custom click-popover /
/// right-click-copy cells, this just adds a hover tooltip for the full text
/// of wide columns instead of a bespoke popover.
/// </summary>
public partial class ActivityLogWindow : Window
{
    private static ActivityLogWindow? _instance;
    public static ActivityLogWindow Instance => _instance ??= new ActivityLogWindow();

    private readonly ObservableCollection<ActivityRow> _rows = [];
    private readonly DispatcherTimer _idleTimer;

    private ActivityLogWindow()
    {
        InitializeComponent();
        Grid.ItemsSource = _rows;
        foreach (var entry in ActivityLog.Snapshot())
            _rows.Add(new ActivityRow(entry));
        if (_rows.Count > 0) Grid.ScrollIntoView(_rows[^1]);

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _idleTimer.Tick += (_, _) =>
        {
            _idleTimer.Stop();
            StatusDot.Fill = Brushes.Gray;
            BannerText.Text = "Idle";
        };

        // A closed WPF Window can never be shown again (Show() throws) -
        // unlike an NSWindow, which is what macOS's ActivityWindowController
        // relies on. Cancel the real close and Hide() instead, so clicking
        // the window's own "X" button behaves like "dismiss" rather than
        // "destroy the reusable singleton" - the next `log show` must still
        // be able to bring it back, as documented.
        Closing += (_, e) =>
        {
            e.Cancel = true;
            UICtlGate.SetWindowOpen(false);
            Hide();
        };
    }

    /// <summary>
    /// UICtlGate only enforces the checkbox's state while this window is
    /// open - see its doc comment - so every path that brings the window on
    /// screen needs to mark it open here rather than relying on the
    /// constructor (this instance is reused across repeated `log show`
    /// calls, not recreated).
    /// </summary>
    public void ShowAndActivate()
    {
        UICtlGate.SetWindowOpen(true);
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        // Plain Activate() from a background process is routinely ignored by
        // Windows' foreground-lock heuristic - reuse the same
        // AttachThreadInput-based trick the rest of uictl already relies on
        // to reliably raise a window it doesn't already own foreground focus
        // for (see AppsAndWindows.BringToFront's doc comment).
        var hwnd = new WindowInteropHelper(this).Handle;
        AppsAndWindows.BringToFront(hwnd);
    }

    /// <summary>Appends a freshly-recorded entry and refreshes the banner. Called via ActivityLog.OnRecord, already marshaled onto the UI thread by the caller.</summary>
    public void Append(ActivityEntry entry)
    {
        _rows.Add(new ActivityRow(entry));
        // Match ActivityLog's own cap - otherwise a long-running daemon's
        // on-screen row list grows unbounded even though the backing log
        // (and its snapshots/exports) evict older entries at the same size.
        if (_rows.Count > ActivityLog.MaxEntries) _rows.RemoveAt(0);
        Grid.ScrollIntoView(_rows[^1]);

        StatusDot.Fill = entry.Ok ? Brushes.LimeGreen : Brushes.Red;
        BannerText.Text = $"Active: {entry.Command}";
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private void GateCheckBox_Changed(object sender, RoutedEventArgs e) =>
        UICtlGate.SetToggleEnabled(GateCheckBox.IsChecked == true);

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = $"uictl-activity-{DateTime.Now:yyyy-MM-ddTHH-mm-ss}.json",
            Filter = "JSON files (*.json)|*.json",
            DefaultExt = ".json",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ActivityLog.ExportJson(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
