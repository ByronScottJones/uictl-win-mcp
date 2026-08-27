using UICtl.Core;

namespace UICtl.Gui;

/// <summary>Display-formatted wrapper around an ActivityEntry for the log window's DataGrid.</summary>
internal sealed class ActivityRow(ActivityEntry entry)
{
    public string TimeText { get; } = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
    public string Command { get; } = entry.Command;
    public string OkText { get; } = entry.Ok ? "✓" : "✗";
    public string DurationText { get; } = entry.DurationMs.ToString("F0");
    public string ParamsSummary { get; } = entry.ParamsSummary;
    public string ResponseSummary { get; } = entry.ResponseSummary;
}
