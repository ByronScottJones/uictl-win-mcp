using System.Diagnostics;

namespace UICtl.Core;

/// <summary>
/// Resolves a GitHub token for the (optional) duplicate-issue check, without
/// requiring the user to do anything if they already have working GitHub
/// credentials on this machine. Mirrors macOS's GitHubToken.swift.
/// </summary>
public static class GitHubToken
{
    /// <summary>
    /// Precedence: an explicit <c>--token</c>/<c>token</c> param, then the
    /// standard <c>GITHUB_TOKEN</c> environment variable, then whatever the
    /// `gh` CLI (if installed and already authenticated - as it commonly is
    /// on a dev machine) has stored. Returns null if none of those pan out,
    /// so callers can degrade gracefully (skip the check) instead of failing.
    /// </summary>
    public static string? Resolve(string? explicitToken)
    {
        if (!string.IsNullOrEmpty(explicitToken)) return explicitToken;

        string? envToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(envToken)) return envToken;

        return FromGhCli();
    }

    private static string? FromGhCli()
    {
        try
        {
            var psi = new ProcessStartInfo("gh", "auth token")
            {
                // stderr is deliberately left un-redirected (inherits this
                // process's handle) rather than piped-and-ignored - a piped
                // stream nobody drains can fill its OS buffer and block `gh`
                // mid-write, which would then hang WaitForExit below forever.
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return null;

            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                return null;
            }
            if (process.ExitCode != 0) return null;

            string token = output.Trim();
            return token.Length == 0 ? null : token;
        }
        catch
        {
            return null;
        }
    }
}
