using System.Windows;
using UICtl.Core;

namespace UICtl.Gui;

/// <summary>
/// Wires the toast and log window up to every recorded call. Called once at
/// daemon startup, right after the WPF Application is constructed and before
/// its message loop starts. Mirrors macOS's GUI/ActivityUI.swift.
/// </summary>
public static class ActivityUI
{
    public static void Install()
    {
        // Force both singletons to construct now, while the accept loop
        // hasn't started yet and ActivityLog.Snapshot() is still empty (the
        // caller doesn't start accepting connections until Install returns -
        // see DaemonCommands.RunForegroundWithGui). Without this, the very
        // first recorded entry can appear twice: ActivityLogWindow's own
        // constructor eagerly loads ActivityLog.Snapshot() (which, by the
        // time OnRecord fires, already includes that entry - Record() adds
        // to the shared list before invoking OnRecord), and then the
        // explicit Append below for that same entry adds it a second time.
        _ = ToastWindow.Instance;
        _ = ActivityLogWindow.Instance;

        ActivityLog.OnRecord = entry =>
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ToastWindow.Instance.ShowFor(entry);
                ActivityLogWindow.Instance.Append(entry);
            });
        };

        ActivityLog.ShowWindow = () =>
        {
            Application.Current?.Dispatcher.BeginInvoke(() => ActivityLogWindow.Instance.ShowAndActivate());
        };
    }
}
