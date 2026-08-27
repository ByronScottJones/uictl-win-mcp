using System.CommandLine;
using System.Threading;
using UICtl.Ipc;

namespace UICtl.Cli.Commands;

internal static class DaemonCommands
{
    public static Command Daemon()
    {
        var cmd = new Command("daemon", "Manage the background uictl daemon that holds the UI Automation engine and element-id state.");
        cmd.Add(Start());
        cmd.Add(Stop());
        cmd.Add(Status());
        return cmd;
    }

    private static Command Start()
    {
        var foreground = new Option<bool>("--foreground")
        {
            Description = "Run in the foreground instead of spawning a detached background process. Used internally by other commands to bootstrap the daemon.",
        };

        var cmd = new Command("start", "Start the daemon (auto-invoked by any other command if it isn't already running).");
        cmd.Add(foreground);
        cmd.SetAction(async (pr, ct) =>
        {
            if (pr.GetValue(foreground))
            {
                RunForegroundWithGui(ct);
                return 0;
            }

            DaemonClient.EnsureRunning();
            return CliRunner.Print(Envelope.Success(new Dictionary<string, object?> { ["started"] = true }));
        });
        return cmd;
    }

    /// <summary>
    /// A dedicated STA thread owns the WPF Application/message pump (the
    /// toast + activity-log window need one) while the accept loop - and all
    /// the socket/DPI/log-file setup DaemonServer.RunForegroundAsync already
    /// does - runs on its own background thread. Mirrors macOS's
    /// DaemonServer.run(): install the GUI hooks, only then start accepting
    /// connections, then let the "app" own this thread until it exits -
    /// except here that's a dedicated UI thread joined from here, rather
    /// than literally this method's own thread, since WPF's message pump
    /// needs STA and this method's thread isn't guaranteed to be one.
    /// </summary>
    private static void RunForegroundWithGui(CancellationToken ct)
    {
        using var uiReady = new ManualResetEventSlim(false);
        Exception? uiException = null;

        var uiThread = new Thread(() =>
        {
            try
            {
                var app = new System.Windows.Application();
                // A bug in the toast/activity-log window (a bad binding, a
                // layout exception, ...) must never take the whole daemon
                // down with it - automation via the accept loop is the part
                // that actually matters. Mark it handled and keep going;
                // Console is redirected to daemon.log by the time this can
                // realistically fire (see RunForegroundAsync).
                app.DispatcherUnhandledException += (_, args) =>
                {
                    Console.WriteLine($"[{DateTime.UtcNow:O}] GUI thread exception (toast/activity-log window) - continuing, automation is unaffected: {args.Exception}");
                    args.Handled = true;
                };
                // If the accept loop's own token is cancelled (e.g. Ctrl+C on
                // a `daemon start --foreground` run directly in a terminal,
                // as opposed to the usual `daemon stop` path, which exits the
                // whole process via Environment.Exit and never reaches this),
                // the accept loop stops but nothing would otherwise tell this
                // thread's message pump to stop too - Dispatcher.InvokeShutdown
                // is safe to call cross-thread for exactly this.
                ct.Register(() => app.Dispatcher.InvokeShutdown());
                UICtl.Gui.ActivityUI.Install();
                uiReady.Set();
                app.Run();
            }
            catch (Exception ex)
            {
                uiException = ex;
                uiReady.Set();
            }
        });
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        uiReady.Wait();
        if (uiException is not null) throw uiException;

        var acceptThread = new Thread(() => DaemonServer.RunForegroundAsync(ct).GetAwaiter().GetResult())
        {
            IsBackground = true,
            Name = "uictl.accept",
        };
        acceptThread.Start();

        uiThread.Join();
    }

    private static Command Stop()
    {
        var cmd = new Command("stop", "Stop the running daemon.");
        cmd.SetAction((_) =>
        {
            if (!DaemonClient.IsRunning())
                return CliRunner.Print(Envelope.Success(new Dictionary<string, object?> { ["stopped"] = false, ["reason"] = "not running" }));

            try
            {
                return CliRunner.Print(DaemonClient.RequestStop());
            }
            catch (Exception ex)
            {
                return CliRunner.Print(Envelope.Failure(ex.Message));
            }
        });
        return cmd;
    }

    private static Command Status()
    {
        var cmd = new Command("status", "Report whether the daemon is running.");
        cmd.SetAction((_) =>
        {
            bool running = DaemonClient.IsRunning();
            return CliRunner.Print(Envelope.Success(new Dictionary<string, object?> { ["running"] = running, ["pipe"] = $@"\\.\pipe\{DaemonPipeName}" }));
        });
        return cmd;
    }

    // Kept in sync with DaemonPaths.PipeName by hand (Ipc's DaemonPaths type is
    // internal to that assembly) - just a display string for `daemon status`.
    private const string DaemonPipeName = "uictl";
}
