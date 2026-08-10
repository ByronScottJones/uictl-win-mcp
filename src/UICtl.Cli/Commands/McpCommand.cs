using System.CommandLine;
using UICtl.Mcp;

namespace UICtl.Cli.Commands;

internal static class McpCommand
{
    public static Command Build()
    {
        var cmd = new Command("mcp", "Run as an MCP server over stdio, exposing every capability as an MCP tool (add via `claude mcp add uictl -- uictl.exe mcp`).");
        cmd.SetAction(async (_, ct) => { await McpServerHost.RunAsync(ct); return 0; });
        return cmd;
    }
}
