using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace UICtl.Mcp;

/// <summary>
/// The 17 uictl_* tools, one per row of MCP_INTERFACE.md's table. Each maps to
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
    ];
}
