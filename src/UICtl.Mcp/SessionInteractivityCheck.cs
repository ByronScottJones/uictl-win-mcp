using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UICtl.Ipc;

namespace UICtl.Mcp;

/// <summary>
/// Best-effort, once-per-process warning when the daemon isn't running in an
/// interactive desktop session - the classic failure mode when it gets
/// auto-spawned from a non-interactive channel, e.g. the first uictl command
/// arriving over an SSH session on Windows (sshd, by default, does not
/// attach spawned processes to the interactively logged-in session's
/// desktop). UI Automation, SendInput, and screen capture then silently
/// fail or no-op - blank screenshots, clicks that land nowhere - instead of
/// raising an obvious error, which is exactly the failure mode this exists
/// to head off. See MCP_INTERFACE.md's "Remote testing over SSH" note.
///
/// Lives outside ToolDefinitions/McpServerHost's generic forward-to-the-
/// daemon path for the same reason FeedbackSubmission does: this needs the
/// live McpServer for *this* request to elicit the connected human, which
/// the daemon (talking only over a named pipe) has no access to.
/// </summary>
internal static class SessionInteractivityCheck
{
    private static readonly TimeSpan ElicitationTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Guards against warning more than once per process - Interlocked.Exchange makes the "have we already tried" check race-safe if two tool calls land concurrently.</summary>
    private static int _alreadyChecked;

    /// <summary>Tools that never touch the interactive desktop - the daemon works fine for these regardless of session interactivity, so there's nothing to warn about.</summary>
    private static readonly HashSet<string> DesktopIndependentTools =
    [
        "uictl_permissions",
        "uictl_apps",
        "uictl_displays",
        "uictl_feedback_create",
        "uictl_feedback_list",
        "uictl_feedback_get",
        "uictl_feedback_update",
        "uictl_feedback_delete",
        "uictl_feedback_check_duplicates",
        "uictl_feedback_submit",
        "uictl_log_export",
    ];

    public static async Task WarnIfNonInteractiveAsync(McpServer server, string? toolName, CancellationToken ct)
    {
        if (toolName is null || DesktopIndependentTools.Contains(toolName)) return;
        if (Interlocked.Exchange(ref _alreadyChecked, 1) != 0) return;

        bool interactive;
        try
        {
            var argsElement = JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement>());
            string response = DaemonClient.Send("permissions.status", argsElement);
            using var doc = JsonDocument.Parse(response);
            interactive = doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean()
                && doc.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("interactive", out var flag)
                && flag.GetBoolean();
        }
        catch
        {
            return; // couldn't even ask - don't compound the problem with a second failure
        }
        if (interactive) return;

        const string message =
            "The uictl daemon on this machine doesn't appear to be running in an interactive desktop session, " +
            "so UI Automation, SendInput, and screen capture calls may silently fail or no-op (blank screenshots, " +
            "clicks that land nowhere) instead of raising a clear error. This is the classic failure mode when the " +
            "daemon gets auto-started from a non-interactive channel - e.g. the first uictl command arriving over " +
            "an SSH session on Windows, which by default doesn't attach new processes to the interactive desktop.\n\n" +
            "To fix: on this machine, log in at the console or via RDP, then either run \"uictl daemon stop\" " +
            "(in case a non-interactive daemon is already running) followed by \"uictl daemon start\" from that " +
            "interactive session, or set up a Scheduled Task running \"uictl.exe daemon start --foreground\" with " +
            "\"Run only when user is logged on\" so it starts automatically at your next login. Leave that session " +
            "logged in (locking the screen is fine, signing out is not) for the daemon to keep working - other " +
            "callers, including ones connecting over SSH, will keep talking to it over the same named pipe once " +
            "it's backed by a desktop-capable process.";

        try
        {
            using var timeoutCts = new CancellationTokenSource(ElicitationTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
            await server.ElicitAsync(new ElicitRequestParams
            {
                Message = message,
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>(),
                    Required = [],
                },
            }, linked.Token);
        }
        catch
        {
            // Client doesn't support elicitation, or didn't respond in time -
            // at least get this into the daemon's own log so it isn't lost.
            Console.WriteLine($"[{DateTime.UtcNow:O}] WARNING: {message}");
        }
    }
}
