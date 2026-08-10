using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UICtl.Ipc;

namespace UICtl.Mcp;

/// <summary>
/// Exposes every uictl capability as an MCP tool over stdio. Each tool handler
/// is a thin translator that forwards to the same DaemonClient the CLI
/// subcommands use, so element ids, the running daemon, and its permission
/// state are shared identically whether a caller drives uictl through the CLI
/// or through MCP - mirrors Sources/uictl/MCP/MCPServer.swift on the macOS
/// sibling project.
/// </summary>
public static class McpServerHost
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "uictl", Version = "0.1.0" },
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult
                {
                    Tools = ToolDefinitions.All.Select(t => t.Tool).ToList(),
                }),
                CallToolHandler = (context, _) => ValueTask.FromResult(HandleCallTool(context.Params)),
            },
        };

        var transport = new StdioServerTransport(options);
        var server = McpServer.Create(transport, options);
        await server.RunAsync(ct);
    }

    private static CallToolResult HandleCallTool(CallToolRequestParams? request)
    {
        var spec = request is not null ? ToolDefinitions.All.FirstOrDefault(t => t.Tool.Name == request.Name) : null;
        if (spec is null)
            return ErrorResult($"unknown tool \"{request?.Name}\"");

        var argsElement = JsonSerializer.SerializeToElement(request!.Arguments ?? new Dictionary<string, JsonElement>());
        string responseJson = DaemonClient.Send(spec.Command, argsElement);

        bool ok = JsonDocument.Parse(responseJson).RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = responseJson }],
            IsError = !ok,
        };
    }

    private static CallToolResult ErrorResult(string message) => new()
    {
        Content = [new TextContentBlock { Text = Envelope.Failure(message) }],
        IsError = true,
    };
}
