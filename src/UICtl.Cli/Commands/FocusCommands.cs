using System.CommandLine;

namespace UICtl.Cli.Commands;

internal static class FocusCommands
{
    public static Command Focus()
    {
        var cmd = new Command("focus", "Pin uictl to one window so click/move/scroll/type/key re-assert its foreground status before acting.");
        cmd.Add(Hold());
        cmd.Add(Release());
        cmd.Add(Status());
        return cmd;
    }

    private static Command Hold()
    {
        var app = new Option<string?>("--app") { Description = "App to hold focus on (name substring, package family name, or pid)." };
        var window = new Option<long?>("--window") { Description = "Window id to hold focus on (from `windows`), instead of --app." };

        var cmd = new Command("hold", "Pin uictl to a window - click/move/scroll/type/key will re-activate it first if a human's own input steals foreground status.");
        cmd.Add(app);
        cmd.Add(window);
        cmd.SetAction(async (pr, ct) =>
        {
            var args = new Dictionary<string, object?>();
            if (pr.GetValue(app) is { } a) args["app"] = a;
            if (pr.GetValue(window) is { } w) args["window"] = w;
            return await CliRunner.RunAsync("focus.hold", args, ct);
        });
        return cmd;
    }

    private static Command Release()
    {
        var cmd = new Command("release", "Stop pinning focus and restore whatever was frontmost right before `hold`.");
        cmd.SetAction(async (_, ct) => await CliRunner.RunAsync("focus.release", new Dictionary<string, object?>(), ct));
        return cmd;
    }

    private static Command Status()
    {
        var cmd = new Command("status", "Show what's currently held, if anything, and what `release` will restore focus to.");
        cmd.SetAction(async (_, ct) => await CliRunner.RunAsync("focus.status", new Dictionary<string, object?>(), ct));
        return cmd;
    }
}
