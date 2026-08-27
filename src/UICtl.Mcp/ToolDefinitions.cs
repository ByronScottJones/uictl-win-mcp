using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace UICtl.Mcp;

/// <summary>
/// The 29 uictl_* tools, one per row of MCP_INTERFACE.md's table. Each maps to
/// the same CommandDispatcher command string the CLI's subcommands will use,
/// so element ids, the daemon, and permission state are shared identically
/// whether a caller drives uictl through the CLI or through MCP - mirrors
/// Sources/uictl/MCP/MCPServer.swift on the macOS sibling project.
/// </summary>
internal sealed record ToolSpec(string Command, Tool Tool);

internal static class ToolDefinitions
{
    private static JsonElement Prop(string type, string description) =>
        JsonSerializer.SerializeToElement(new { type, description });

    private static JsonElement Schema(object properties, string[]? required = null) =>
        JsonSerializer.SerializeToElement(new { type = "object", properties, required = required ?? [] });

    public static readonly IReadOnlyList<ToolSpec> All =
    [
        new ToolSpec("permissions.status",
            new Tool
            {
                Name = "uictl_permissions",
                Description = "Check elevation status: this process's own, and optionally a target app's. Windows' analog of macOS's Accessibility/Screen Recording permission check - UI Automation and SendInput are blocked by UIPI when the target outranks uictl.",
                InputSchema = Schema(new
                {
                    app = Prop("string", "Name substring, package family name, or pid of a process to also check elevation for."),
                }),
            }),

        new ToolSpec("apps.list",
            new Tool
            {
                Name = "uictl_apps",
                Description = "List running processes.",
                InputSchema = Schema(new
                {
                    all = Prop("boolean", "Include background/service processes, not just regular foreground apps."),
                }),
            }),

        new ToolSpec("displays.list",
            new Tool
            {
                Name = "uictl_displays",
                Description = "List monitors: index (matches screenshot's `screen` arg), bounds in virtual-screen space, the primary-monitor flag, and a DPI-derived scale factor.",
                InputSchema = Schema(new { }),
            }),

        new ToolSpec("windows.list",
            new Tool
            {
                Name = "uictl_windows",
                Description = "List on-screen windows, optionally filtered to one app.",
                InputSchema = Schema(new
                {
                    app = Prop("string", "Name substring, package family name, or pid."),
                }),
            }),

        new ToolSpec("activate",
            new Tool
            {
                Name = "uictl_activate",
                Description = "Bring an application (and optionally a specific window) to the front.",
                InputSchema = Schema(new
                {
                    app = Prop("string", "Name substring, package family name, or pid."),
                    window = Prop("integer", "Window id to also raise."),
                }, required: ["app"]),
            }),

        new ToolSpec("focus.hold",
            new Tool
            {
                Name = "uictl_focus_hold",
                Description = "Pin uictl to one window - click/move/scroll/type/key will re-activate it first if a human's own input steals foreground status. Requires either app or window.",
                InputSchema = Schema(new
                {
                    app = Prop("string", "Name substring, package family name, or pid."),
                    window = Prop("integer", "Window id, instead of app."),
                }),
            }),

        new ToolSpec("focus.release",
            new Tool
            {
                Name = "uictl_focus_release",
                Description = "Stop pinning focus and restore whatever was frontmost right before the hold.",
                InputSchema = Schema(new { }),
            }),

        new ToolSpec("focus.status",
            new Tool
            {
                Name = "uictl_focus_status",
                Description = "Show what's currently held, if anything, and what release will restore focus to.",
                InputSchema = Schema(new { }),
            }),

        new ToolSpec("screenshot",
            new Tool
            {
                Name = "uictl_screenshot",
                Description = "Capture a window, an app's frontmost window, or a display. Set annotate=true to overlay numbered boxes on every UI Automation element and get a legend back - then click/type by element number.",
                InputSchema = Schema(new
                {
                    window = Prop("integer", "Window id (from uictl_windows)."),
                    app = Prop("string", "App whose frontmost window to capture."),
                    screen = Prop("integer", "Display index to capture instead of a window."),
                    @out = Prop("string", "Output PNG path. Defaults under %LOCALAPPDATA%\\uictl\\screenshots\\."),
                    annotate = Prop("boolean", "Overlay numbered element boxes and return a legend."),
                    role = Prop("string", "When annotating, only include elements with this UI Automation ControlType (e.g. Button)."),
                }),
            }),

        new ToolSpec("elements",
            new Tool
            {
                Name = "uictl_elements",
                Description = "Walk a window's UI Automation tree; returns each element's id, role (ControlType), title, value, and frame.",
                InputSchema = Schema(new
                {
                    window = Prop("integer", "Window id (from uictl_windows)."),
                    app = Prop("string", "App whose frontmost window to inspect."),
                    role = Prop("string", "Only include elements with this ControlType (e.g. Button, Edit)."),
                    title = Prop("string", "Only include elements whose name contains this."),
                    maxDepth = Prop("integer", "Maximum tree depth."),
                    maxElements = Prop("integer", "Stop after this many matches."),
                }),
            }),

        new ToolSpec("click",
            new Tool
            {
                Name = "uictl_click",
                Description = "Click at a screen point or on a specific element (by id from uictl_elements/uictl_screenshot).",
                InputSchema = Schema(new
                {
                    at = Prop("string", "\"x,y\" screen point."),
                    element = Prop("string", "Element id."),
                    button = Prop("string", "left | right | center"),
                    @double = Prop("boolean", "Double-click."),
                    count = Prop("integer", "Click count (e.g. 3 for triple-click)."),
                }),
            }),

        new ToolSpec("move",
            new Tool
            {
                Name = "uictl_move",
                Description = "Move the mouse cursor without clicking.",
                InputSchema = Schema(new { at = Prop("string", "\"x,y\" screen point.") }, required: ["at"]),
            }),

        new ToolSpec("scroll",
            new Tool
            {
                Name = "uictl_scroll",
                Description = "Scroll at a screen point.",
                InputSchema = Schema(new
                {
                    at = Prop("string", "\"x,y\" screen point."),
                    dx = Prop("integer", "Horizontal delta (positive scrolls left)."),
                    dy = Prop("integer", "Vertical delta (positive scrolls up)."),
                }, required: ["at"]),
            }),

        new ToolSpec("type",
            new Tool
            {
                Name = "uictl_type",
                Description = "Type text into the focused control, or into a specific element by id (tries UI Automation's ValuePattern.SetValue first, falls back to focus + synthesized keystrokes).",
                InputSchema = Schema(new
                {
                    text = Prop("string", "Text to type."),
                    element = Prop("string", "Element id to type into."),
                }, required: ["text"]),
            }),

        new ToolSpec("key",
            new Tool
            {
                Name = "uictl_key",
                Description = "Send a keyboard shortcut, e.g. \"ctrl+shift+esc\".",
                InputSchema = Schema(new
                {
                    combo = Prop("string", "Modifiers joined with +: ctrl, shift, alt, win."),
                }, required: ["combo"]),
            }),

        new ToolSpec("waitFor",
            new Tool
            {
                Name = "uictl_wait_for",
                Description = "Block until an element matching role/title appears in a window, or timeout elapses.",
                InputSchema = Schema(new
                {
                    window = Prop("integer", "Window id."),
                    app = Prop("string", "App whose frontmost window to watch."),
                    role = Prop("string", "ControlType to match."),
                    title = Prop("string", "Name substring to match."),
                    timeout = Prop("number", "Seconds to wait (default 10)."),
                }),
            }),

        new ToolSpec("ocr",
            new Tool
            {
                Name = "uictl_ocr",
                Description = "Recognize text in an image file or a window, optionally restricted to a region. Note: confidence is always null on Windows - Windows.Media.Ocr reports no per-result score.",
                InputSchema = Schema(new
                {
                    image = Prop("string", "PNG path, instead of capturing a window."),
                    window = Prop("integer", "Window id to capture and OCR."),
                    app = Prop("string", "App whose frontmost window to capture and OCR."),
                    region = Prop("string", "\"x,y,w,h\" to restrict OCR to."),
                }),
            }),

        new ToolSpec("pixel",
            new Tool
            {
                Name = "uictl_pixel",
                Description = "Sample the color of a single screen pixel.",
                InputSchema = Schema(new { at = Prop("string", "\"x,y\" screen point.") }, required: ["at"]),
            }),

        new ToolSpec("clipboard.get",
            new Tool
            {
                Name = "uictl_clipboard_get",
                Description = "Read the system clipboard text.",
                InputSchema = Schema(new { }),
            }),

        new ToolSpec("clipboard.set",
            new Tool
            {
                Name = "uictl_clipboard_set",
                Description = "Write text to the system clipboard.",
                InputSchema = Schema(new { text = Prop("string", "Text to place on the clipboard.") }, required: ["text"]),
            }),

        new ToolSpec("feedback.create",
            new Tool
            {
                Name = "uictl_feedback_create",
                Description = "Draft a new local feedback entry (an issue, error, or recommendation about uictl itself) - stored locally, not sent anywhere. Use uictl_feedback_submit to send it to GitHub.",
                InputSchema = Schema(new
                {
                    category = Prop("string", "One of: issue, error, recommendation."),
                    title = Prop("string", "Short summary - becomes the GitHub issue title."),
                    body = Prop("string", "Full description - becomes the GitHub issue body."),
                }, required: ["category", "title", "body"]),
            }),

        new ToolSpec("feedback.list",
            new Tool
            {
                Name = "uictl_feedback_list",
                Description = "List all local feedback entries (drafts and previously submitted).",
                InputSchema = Schema(new { }),
            }),

        new ToolSpec("feedback.get",
            new Tool
            {
                Name = "uictl_feedback_get",
                Description = "Show one local feedback entry in full.",
                InputSchema = Schema(new { id = Prop("integer", "Feedback entry id (from uictl_feedback_list).") }, required: ["id"]),
            }),

        new ToolSpec("feedback.update",
            new Tool
            {
                Name = "uictl_feedback_update",
                Description = "Edit a local feedback entry.",
                InputSchema = Schema(new
                {
                    id = Prop("integer", "Feedback entry id (from uictl_feedback_list)."),
                    category = Prop("string", "One of: issue, error, recommendation."),
                    title = Prop("string", "New title."),
                    body = Prop("string", "New body."),
                }, required: ["id"]),
            }),

        new ToolSpec("feedback.delete",
            new Tool
            {
                Name = "uictl_feedback_delete",
                Description = "Delete a local feedback entry.",
                InputSchema = Schema(new { id = Prop("integer", "Feedback entry id (from uictl_feedback_list).") }, required: ["id"]),
            }),

        new ToolSpec("feedback.checkDuplicates",
            new Tool
            {
                Name = "uictl_feedback_check_duplicates",
                Description = "Check a local feedback entry's title against existing GitHub issues (open and closed), without submitting anything. Needs a token if the repo is private (pass \"token\", set $GITHUB_TOKEN, or have `gh` already authenticated) - otherwise reports checked:false rather than blocking. uictl_feedback_submit runs this same check automatically before opening anything.",
                InputSchema = Schema(new
                {
                    id = Prop("integer", "Feedback entry id (from uictl_feedback_list)."),
                    repo = Prop("string", "GitHub repo to check against, as \"owner/repo\". Defaults to uictl's own repo."),
                    token = Prop("string", "GitHub token, if the repo needs one."),
                }, required: ["id"]),
            }),

        new ToolSpec("log.show",
            new Tool
            {
                Name = "uictl_log_show",
                Description = "Open the on-screen activity log window, showing a live list of every CLI/MCP call this daemon has handled - useful to let whoever is at this machine see what's being automated. If this window gets buried under others, call this again to bring it back to front.",
                InputSchema = Schema(new { }),
            }),

        new ToolSpec("log.export",
            new Tool
            {
                Name = "uictl_log_export",
                Description = "Export the daemon's activity log (every CLI/MCP call it has handled, most recent ~2000) as a JSON file. Params/response values are the same summarized form shown in uictl_log_show's table: JSON-encoded, truncated to ~4000 characters, with clipboard/typed-text content redacted to a length placeholder - not necessarily the exact raw payload for calls whose output was longer than that (e.g. large uictl_elements/uictl_ocr results).",
                InputSchema = Schema(new
                {
                    @out = Prop("string", "Output JSON path. Defaults under %LOCALAPPDATA%\\uictl\\exports\\."),
                }),
            }),

        // Handled specially in McpServerHost's CallTool dispatch (MCP
        // elicitation, not a plain daemon forward) - see FeedbackSubmission.cs.
        // Still listed here so ListTools advertises it with the same schema.
        new ToolSpec("feedback.submit",
            new Tool
            {
                Name = "uictl_feedback_submit",
                Description = "Send a local feedback entry to GitHub Issues by opening a pre-filled \"new issue\" page - it does not create the issue itself, a human still reviews and clicks \"Create\" there. First checks the entry's title against existing GitHub issues (see uictl_feedback_check_duplicates); if a likely duplicate is found, the local entry is deleted and nothing is opened or elicited. Otherwise, since this is you (an agent) initiating something outward-facing on the human's behalf, this asks the human to review (and optionally edit) the content via MCP elicitation first, then hands off the pre-filled URL via a second, URL-mode elicitation. If the connected client doesn't support elicitation, it falls back to opening the URL directly on this machine.",
                InputSchema = Schema(new
                {
                    id = Prop("integer", "Feedback entry id (from uictl_feedback_list)."),
                    repo = Prop("string", "GitHub repo to submit to, as \"owner/repo\". Defaults to uictl's own repo."),
                    token = Prop("string", "GitHub token, for the duplicate check against a private repo."),
                }, required: ["id"]),
            }),
    ];
}
