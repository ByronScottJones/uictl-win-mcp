using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using UICtl.Core;

namespace UICtl.Ipc;

/// <summary>
/// Sends one JSON request and reads one JSON response per call, auto-spawning
/// the daemon if the pipe isn't reachable. Used by both CLI subcommands and
/// MCP tool handlers, so element ids and permission state are shared no
/// matter which front end a caller used to get there - see ENGINEERING.md.
/// </summary>
public static class DaemonClient
{
    private const int SpawnWaitMs = 5000;

    public static string Send(string command, JsonElement @params)
    {
        using var pipe = Connect();
        string request = JsonSerializer.Serialize(new { command, @params }, JsonOptions.Default);
        Framing.WriteMessageAsync(pipe, request).GetAwaiter().GetResult();
        return Framing.ReadMessageAsync(pipe).GetAwaiter().GetResult();
    }

    public static bool IsRunning()
    {
        var pipe = TryConnectOnce(200);
        pipe?.Dispose();
        return pipe is not null;
    }

    public static void RequestStop()
    {
        using var pipe = TryConnectOnce(500) ?? throw new UiCtlException("the daemon is not running");
        string request = JsonSerializer.Serialize(new { command = "__daemon_stop__", @params = ParamsExtensions.Empty }, JsonOptions.Default);
        Framing.WriteMessageAsync(pipe, request).GetAwaiter().GetResult();
        Framing.ReadMessageAsync(pipe).GetAwaiter().GetResult();
    }

    private static NamedPipeClientStream Connect()
    {
        if (TryConnectOnce(200) is { } pipe) return pipe;

        SpawnDaemon();

        var deadline = DateTime.UtcNow.AddMilliseconds(SpawnWaitMs);
        while (DateTime.UtcNow < deadline)
        {
            if (TryConnectOnce(250) is { } spawned) return spawned;
            Thread.Sleep(100);
        }
        throw new UiCtlException($"could not reach the uictl daemon after spawning it - check {DaemonPaths.LogPath}");
    }

    private static NamedPipeClientStream? TryConnectOnce(int timeoutMs)
    {
        var pipe = new NamedPipeClientStream(".", DaemonPaths.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            pipe.Connect(timeoutMs);
            return pipe;
        }
        catch (Exception)
        {
            pipe.Dispose();
            return null;
        }
    }

    private static void SpawnDaemon()
    {
        string exePath = Environment.ProcessPath ?? throw new UiCtlException("could not determine this process's own path to spawn the daemon");
        Directory.CreateDirectory(DaemonPaths.BaseDir);

        var startInfo = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("daemon");
        startInfo.ArgumentList.Add("start");
        startInfo.ArgumentList.Add("--foreground");

        _ = Process.Start(startInfo) ?? throw new UiCtlException("failed to start the daemon process");
    }
}
