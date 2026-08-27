using System.CommandLine;

namespace UICtl.Cli.Commands;

internal static class LogCommands
{
    public static Command Log()
    {
        var cmd = new Command("log", "Show or export the daemon's activity log of every CLI/MCP call it has handled.");
        cmd.Add(Show());
        cmd.Add(Export());
        return cmd;
    }

    private static Command Show()
    {
        var cmd = new Command(
            "show",
            "Open the on-screen activity log window. If it gets buried under other windows, run this again to bring it back to front.");
        cmd.SetAction(async (_, ct) => await CliRunner.RunAsync("log.show", new Dictionary<string, object?>(), ct));
        return cmd;
    }

    private static Command Export()
    {
        var outOption = new Option<string?>("--out") { Description = "Output JSON path. Defaults under %LOCALAPPDATA%\\uictl\\exports\\." };

        var cmd = new Command("export", "Export the activity log as JSON.");
        cmd.Add(outOption);
        cmd.SetAction(async (pr, ct) =>
        {
            var args = new Dictionary<string, object?>();
            if (pr.GetValue(outOption) is { } o) args["out"] = o;
            return await CliRunner.RunAsync("log.export", args, ct);
        });
        return cmd;
    }
}
