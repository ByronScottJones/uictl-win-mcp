using System.IO.Pipes;
using System.Text.Json;
using UICtl.Core;

namespace UICtl.Ipc;

/// <summary>
/// Accepts one named-pipe connection at a time and runs each request through
/// CommandDispatcher - see ENGINEERING.md for why one connection at a time
/// (no locking needed around ElementStore/the UI Automation engine).
/// </summary>
public static class DaemonServer
{
    private const string StopCommand = "__daemon_stop__";

    public static async Task RunForegroundAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(DaemonPaths.BaseDir);
        RedirectConsoleToLogFile();

        // Must happen before any window/monitor enumeration - see DpiAwareness's
        // doc comment. Done after the log redirect above so a failure warning
        // (see DpiAwareness.EnsurePerMonitorAware) lands in daemon.log, not a
        // console this auto-spawned process may not have.
        DpiAwareness.EnsurePerMonitorAware();

        Console.WriteLine($"[{DateTime.UtcNow:O}] uictl daemon starting, pipe \\\\.\\pipe\\{DaemonPaths.PipeName}");

        while (!ct.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(DaemonPaths.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await pipe.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (await HandleConnectionAsync(pipe, ct))
                break;
        }

        Console.WriteLine($"[{DateTime.UtcNow:O}] uictl daemon stopped");
    }

    /// <returns>true if this was a stop request and the accept loop should exit.</returns>
    private static async Task<bool> HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            string requestJson = await Framing.ReadMessageAsync(pipe, ct);
            using var requestDoc = JsonDocument.Parse(requestJson);
            var request = requestDoc.RootElement;

            string command = request.TryGetProperty("command", out var c) ? c.GetString() ?? "" : "";
            var paramsElement = request.TryGetProperty("params", out var p) ? p : ParamsExtensions.Empty;

            if (command == StopCommand)
            {
                await Framing.WriteMessageAsync(pipe, Envelope.Success(new Dictionary<string, object?> { ["stopped"] = true }), ct);
                // A hard, immediate exit rather than breaking the accept loop
                // and unwinding "gracefully" - mirrors macOS's DaemonServer.swift,
                // which calls exit(0) directly here. Since Phase 4 this process
                // also owns a dedicated WPF UI thread blocked in Application.Run(),
                // which nothing else would ever signal to shut down otherwise.
                Environment.Exit(0);
                return true;
            }

            string response = CommandDispatcher.Dispatch(command, paramsElement);
            await Framing.WriteMessageAsync(pipe, response, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow:O}] request failed: {ex}");
            try
            {
                await Framing.WriteMessageAsync(pipe, Envelope.Failure(ex.Message), ct);
            }
            catch
            {
                // client already disconnected; nothing to report to
            }
        }
        return false;
    }

    private static void RedirectConsoleToLogFile()
    {
        var writer = new StreamWriter(DaemonPaths.LogPath, append: true) { AutoFlush = true };
        Console.SetOut(writer);
        Console.SetError(writer);
    }
}
