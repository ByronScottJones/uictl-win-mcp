using System.Diagnostics;
using UICtl.Core;

namespace UICtl.Core.Tests.TestSupport;

/// <summary>
/// Launches one real Notepad process for the lifetime of a test collection and
/// waits for its main window to actually exist, so tests that need a live
/// window/element don't each pay Notepad's startup cost or race its window
/// showing up. Disposal kills the process - never leave a stray Notepad
/// window behind after a test run.
///
/// Modern Windows ships Notepad as an MSIX-packaged app: Process.Start's
/// returned pid is a launcher stub that exits immediately once the real
/// window is up under a *different* pid (see [[uictl-dev-machine]] in
/// project memory) - so this polls Process.GetProcessesByName("Notepad")
/// for whichever process actually owns a resolvable main window, rather
/// than trusting the pid Process.Start handed back.
/// </summary>
public sealed class NotepadFixture : IDisposable
{
    public int Pid { get; }
    public long WindowId { get; }

    public NotepadFixture()
    {
        // Exclude whatever Notepad instance(s) the developer already had open before this
        // run started - otherwise this fixture can attach to (and Dispose() can kill) a
        // real, pre-existing Notepad window that has nothing to do with this test run.
        (int pid, IntPtr hWnd) = LaunchAndWaitForWindow(GetRunningPids("Notepad"));
        Pid = pid;
        WindowId = hWnd.ToInt64();
    }

    /// <summary>
    /// Launches Paint as a second, independent top-level window - e.g. for tests
    /// that need to switch real foreground focus away from the fixture's
    /// Notepad window. Not another Notepad instance: modern Notepad is
    /// single-instance, so a second launch just refocuses the existing window
    /// rather than producing a second pid/window to switch to. Paint is
    /// classic Win32 and its pid stays stable (see [[uictl-dev-machine]]).
    /// </summary>
    public static (int Pid, long WindowId) LaunchAnotherApp()
    {
        var process = System.Diagnostics.Process.Start(new ProcessStartInfo("mspaint.exe") { UseShellExecute = true })
            ?? throw new InvalidOperationException("failed to start mspaint.exe");

        IntPtr hWnd = IntPtr.Zero;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            hWnd = AppsAndWindows.FindMainWindow(process.Id);
            if (hWnd != IntPtr.Zero) break;
            Thread.Sleep(100);
        }
        if (hWnd == IntPtr.Zero)
        {
            Kill(process.Id); // don't leave a stray, windowless Paint process running after a failed launch
            throw new InvalidOperationException("mspaint.exe never produced a resolvable main window within 10s");
        }
        return (process.Id, hWnd.ToInt64());
    }

    private static HashSet<int> GetRunningPids(string processName)
    {
        var pids = new HashSet<int>();
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(processName))
            using (process) pids.Add(process.Id);
        return pids;
    }

    public static void Kill(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch { /* already gone */ }
    }

    private static (int Pid, IntPtr HWnd) LaunchAndWaitForWindow(IReadOnlySet<int> excludingPids)
    {
        using (var launcher = System.Diagnostics.Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true }))
        {
            // launcher may already have exited by the time we get here - that's expected.
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var candidate in System.Diagnostics.Process.GetProcessesByName("Notepad"))
            {
                using (candidate)
                {
                    if (excludingPids.Contains(candidate.Id)) continue;
                    IntPtr hWnd = AppsAndWindows.FindMainWindow(candidate.Id);
                    if (hWnd != IntPtr.Zero)
                        return (candidate.Id, hWnd);
                }
            }
            Thread.Sleep(100);
        }
        throw new InvalidOperationException("notepad.exe never produced a resolvable main window within 10s");
    }

    public void Dispose() => Kill(Pid);
}

[CollectionDefinition("Notepad app")]
public sealed class NotepadCollection : ICollectionFixture<NotepadFixture>
{
}
