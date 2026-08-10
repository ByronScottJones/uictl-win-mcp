using System.CommandLine;

namespace UICtl.Cli.Commands;

internal static class InputCommands
{
    public static Command Click()
    {
        var at = new Option<string?>("--at") { Description = "Screen point to click: \"x,y\"." };
        var element = new Option<string?>("--element") { Description = "Element id (from `elements` or `screenshot --annotate`) to click the center of." };
        var button = new Option<string?>("--button") { Description = "Mouse button." };
        var doubleClick = new Option<bool>("--double") { Description = "Double-click instead of single-click." };
        var count = new Option<int?>("--count") { Description = "Click count (e.g. 3 for triple-click). Ignored if --double is set." };

        var cmd = new Command("click", "Click at a screen point or on a specific element.");
        cmd.Add(at);
        cmd.Add(element);
        cmd.Add(button);
        cmd.Add(doubleClick);
        cmd.Add(count);
        cmd.SetAction(async (pr, ct) =>
        {
            var args = new Dictionary<string, object?>();
            if (pr.GetValue(at) is { } a) args["at"] = a;
            if (pr.GetValue(element) is { } e) args["element"] = e;
            if (pr.GetValue(button) is { } b) args["button"] = b;
            if (pr.GetValue(doubleClick)) args["double"] = true;
            if (pr.GetValue(count) is { } c) args["count"] = c;
            return await CliRunner.RunAsync("click", args, ct);
        });
        return cmd;
    }

    public static Command Move()
    {
        var at = new Option<string>("--at") { Description = "Screen point to move to: \"x,y\".", Required = true };

        var cmd = new Command("move", "Move the mouse cursor without clicking (e.g. to trigger hover states).");
        cmd.Add(at);
        cmd.SetAction(async (pr, ct) => await CliRunner.RunAsync("move", new() { ["at"] = pr.GetValue(at) }, ct));
        return cmd;
    }

    public static Command Scroll()
    {
        var at = new Option<string>("--at") { Description = "Screen point to scroll at: \"x,y\".", Required = true };
        var dy = new Option<int?>("--dy") { Description = "Vertical scroll delta in pixels (positive scrolls up)." };
        var dx = new Option<int?>("--dx") { Description = "Horizontal scroll delta in pixels (positive scrolls left)." };

        var cmd = new Command("scroll", "Scroll at a screen point.");
        cmd.Add(at);
        cmd.Add(dy);
        cmd.Add(dx);
        cmd.SetAction(async (pr, ct) =>
        {
            var args = new Dictionary<string, object?> { ["at"] = pr.GetValue(at) };
            if (pr.GetValue(dy) is { } y) args["dy"] = y;
            if (pr.GetValue(dx) is { } x) args["dx"] = x;
            return await CliRunner.RunAsync("scroll", args, ct);
        });
        return cmd;
    }

    public static Command Type()
    {
        var text = new Argument<string>("text") { Description = "Text to type." };
        var element = new Option<string?>("--element") { Description = "Element id (from `elements`) to type into." };

        var cmd = new Command("type", "Type text into the focused control, or into a specific element by id.");
        cmd.Add(text);
        cmd.Add(element);
        cmd.SetAction(async (pr, ct) =>
        {
            var args = new Dictionary<string, object?> { ["text"] = pr.GetValue(text) };
            if (pr.GetValue(element) is { } e) args["element"] = e;
            return await CliRunner.RunAsync("type", args, ct);
        });
        return cmd;
    }

    public static Command Key()
    {
        var combo = new Argument<string>("combo") { Description = "Key combo, modifiers joined with \"+\": ctrl, shift, alt, win." };

        var cmd = new Command("key", "Send a keyboard shortcut, e.g. \"ctrl+shift+esc\".");
        cmd.Add(combo);
        cmd.SetAction(async (pr, ct) => await CliRunner.RunAsync("key", new() { ["combo"] = pr.GetValue(combo) }, ct));
        return cmd;
    }
}
