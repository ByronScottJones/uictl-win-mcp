using System.Diagnostics;
using System.Text;
using UICtl.Core.Interop;

namespace UICtl.Core;

/// <summary>Resolves an app selector string to a process id: name substring, then package family name, then numeric pid. See MCP_INTERFACE.md.</summary>
public static class AppSelector
{
    public static int Resolve(string selector)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (NameMatches(process, selector))
                    return process.Id;
            }
        }

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (PackageFamilyNameMatches(process, selector))
                    return process.Id;
            }
        }

        if (int.TryParse(selector, out var pid) && ProcessExists(pid))
            return pid;

        throw new UiCtlException($"no running app matches \"{selector}\"");
    }

    private static bool NameMatches(Process process, string selector)
    {
        try
        {
            return process.ProcessName.Contains(selector, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool PackageFamilyNameMatches(Process process, string selector)
    {
        var family = GetPackageFamilyName(process);
        return family.Length > 0 && family.Equals(selector, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ProcessExists(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The closest Windows analog of a macOS bundle id: "" for classic Win32 apps, which is most of them.</summary>
    internal static string GetPackageFamilyName(Process process)
    {
        try
        {
            uint length = 0;
            NativeMethods.GetPackageFamilyName(process.Handle, ref length, null);
            if (length == 0)
                return "";

            var buffer = new StringBuilder((int)length);
            int result = NativeMethods.GetPackageFamilyName(process.Handle, ref length, buffer);
            return result == 0 ? buffer.ToString() : "";
        }
        catch
        {
            return "";
        }
    }
}
