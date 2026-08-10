using System.CommandLine;

namespace UICtl.Cli.Commands;

internal static class MiscCommands
{
    public static Command WaitFor()
    {
        var window = new Option<long?>("--window") { Description = "Window id (from `windows`). Either this or --app is required." };
        var app = new Option<string?>("--app") { Description = "App whose frontmost window to watch." };
        var role = new Option<string?>("--role") { Description = "ControlType to match (e.g. Button)." };
        var title = new Option<string?>("--title") { Description = "Name substring to match." };
        var timeout = new Option<double?>("--timeout") { Description = "Seconds to wait before giving up." };

        var cmd = new Command("wait-for", "Block until an element matching role/title appears in a window, or timeout elapses.");
        cmd.Add(window);
        cmd.Add(app);
        cmd.Add(role);
        cmd.Add(title);
        cmd.Add(timeout);
        cmd.SetAction(async (pr, ct) =>
        {
            var args = new Dictionary<string, object?>();
            if (pr.GetValue(window) is { } w) args["window"] = w;
            if (pr.GetValue(app) is { } a) args["app"] = a;
            if (pr.GetValue(role) is { } r) args["role"] = r;
            if (pr.GetValue(title) is { } t) args["title"] = t;
            if (pr.GetValue(timeout) is { } to) args["timeout"] = to;
            return await CliRunner.RunAsync("waitFor", args, ct);
        });
        return cmd;
    }

    public static Command Clipboard()
    {
        var get = new Command("get", "Read the system clipboard text.");
        get.SetAction(async (_, ct) => await CliRunner.RunAsync("clipboard.get", [], ct));

        var text = new Argument<string>("text") { Description = "Text to place on the clipboard." };
        var set = new Command("set", "Write text to the system clipboard.");
        set.Add(text);
        set.SetAction(async (pr, ct) => await CliRunner.RunAsync("clipboard.set", new() { ["text"] = pr.GetValue(text) }, ct));

        var cmd = new Command("clipboard", "Read or write the system clipboard.");
        cmd.Add(get);
        cmd.Add(set);
        return cmd;
    }
}
