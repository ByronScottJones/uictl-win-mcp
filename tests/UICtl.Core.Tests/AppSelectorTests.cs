using System.Diagnostics;
using UICtl.Core;

namespace UICtl.Core.Tests;

public class AppSelectorTests
{
    [Fact]
    public void Resolve_ByNumericPid_ReturnsThatPid()
    {
        int ownPid = Environment.ProcessId;
        Assert.Equal(ownPid, AppSelector.Resolve(ownPid.ToString()));
    }

    [Fact]
    public void Resolve_ByNameSubstring_ReturnsAProcessWhoseNameContainsIt()
    {
        string selector = Process.GetCurrentProcess().ProcessName;
        int resolved = AppSelector.Resolve(selector[..Math.Min(4, selector.Length)]);

        using var process = Process.GetProcessById(resolved);
        Assert.Contains(selector[..Math.Min(4, selector.Length)], process.ProcessName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_UnknownSelector_Throws()
    {
        Assert.Throws<UiCtlException>(() => AppSelector.Resolve("no-such-app-selector-xyz-123"));
    }

    [Fact]
    public void Resolve_NonExistentPid_FallsThroughToThrow()
    {
        // A pid that is syntactically numeric but (almost certainly) not running.
        Assert.Throws<UiCtlException>(() => AppSelector.Resolve("999999"));
    }
}
