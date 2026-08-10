using System.CommandLine;

namespace UICtl.Cli.Commands;

internal static class QueryCommands
{
    public static Command Permissions()
    {
        var app = new Option<string?>("--app") { Description = "Name substring, package family name, or pid of a process to also check elevation for." };
        var request = new Option<bool>("--request") { Description = "No-op on Windows (kept for CLI parity with macOS, which uses this to trigger TCC consent prompts)." };

        var cmd = new Command("permissions", "Check elevation status: this process's own, and optionally a target app's.");
        cmd.Add(app);
        cmd.Add(request);
        cmd.SetAction(async (pr, ct) =>
        {
            var args = new Dictionary<string, object?>();
            if (pr.GetValue(app) is { } a) args["app"] = a;
            string command = pr.GetValue(request) ? "permissions.request" : "permissions.status";
            return await CliRunner.RunAsync(command, args, ct);
        });
        return cmd;
    }

    public static Command Apps()
    {
        var all = new Option<bool>("--all") { Description = "Include background/service processes, not just regular foreground apps." };

        var cmd = new Command("apps", "List running applications.");
        cmd.Add(all);
        cmd.SetAction(async (pr, ct) =>
        {
            var args = new Dictionary<string, object?> { ["all"] = pr.GetValue(all) };
            return await CliRunner.RunAsync("apps.list", args, ct);
        });
        return cmd;
    }

    public static Command Windows()
    {
        var app = new Option<string?>("--app") { Description = "Filter to windows owned by this app (name substring, package family name, or pid)." };

        var cmd = new Command("windows", "List on-screen windows, optionally filtered to one app.");
        cmd.Add(app);
        cmd.SetAction(async (pr, ct) =>
        {
            var args = new Dictionary<string, object?>();
            if (pr.GetValue(app) is { } a) args["app"] = a;
            return await CliRunner.RunAsync("windows.list", args, ct);
        });
        return cmd;
    }

    public static Command Activate()
    {
        var app = new Option<string>("--app") { Description = "App to activate (name substring, package family name, or pid).", Required = true };
        var window = new Option<long?>("--window") { Description = "Also raise this specific window id (from `windows`)." };

        var cmd = new Command("activate", "Bring an application (and optionally a specific window) to the front.");
        cmd.Add(app);
        cmd.Add(window);
        cmd.SetAction(async (pr, ct) =>
        {
            var args = new Dictionary<string, object?> { ["app"] = pr.GetValue(app) };
            if (pr.GetValue(window) is { } w) args["window"] = w;
            return await CliRunner.RunAsync("activate", args, ct);
        });
        return cmd;
    }
}
