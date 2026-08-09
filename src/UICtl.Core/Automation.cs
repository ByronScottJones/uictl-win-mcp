using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace UICtl.Core;

/// <summary>
/// UI Automation tree walking and element actions, via FlaUI's wrapper over the
/// native IUIAutomation COM API. See ENGINEERING.md for why FlaUI instead of
/// hand-rolled COM interop or the older managed System.Windows.Automation.
/// </summary>
public static class Automation
{
    // Reused across calls - each UIA3Automation owns its own COM UI Automation
    // session, so this should not be recreated per request.
    private static readonly UIA3Automation Engine = new();

    public static AutomationElement ResolveWindowElement(long windowId)
    {
        var element = Engine.FromHandle(new IntPtr(windowId));
        if (element is null)
            throw new UiCtlException($"could not resolve an automation element for window {windowId}");
        return element;
    }

    public static ElementWalkResult EnumerateElements(long windowId, AutomationElement windowElement, ElementWalkOptions options)
    {
        ElementStore.Reset(windowId);
        var results = new List<ElementInfo>();
        bool truncated = false;
        WalkChildren(windowElement, depth: 1, options, windowId, results, ref truncated);
        return new ElementWalkResult(results, truncated);
    }

    private static void WalkChildren(AutomationElement element, int depth, ElementWalkOptions options, long windowId, List<ElementInfo> results, ref bool truncated)
    {
        AutomationElement[] children;
        try
        {
            children = element.FindAllChildren();
        }
        catch
        {
            return; // an element that throws walking its own children (e.g. mid-teardown) is skipped, not fatal to the whole walk
        }

        foreach (var child in children)
        {
            if (results.Count >= options.MaxElements)
            {
                truncated = true;
                return;
            }

            if (Matches(child, options))
                results.Add(ToElementInfo(ElementStore.Register(windowId, child), child));

            if (depth < options.MaxDepth)
                WalkChildren(child, depth + 1, options, windowId, results, ref truncated);

            if (truncated) return;
        }
    }

    private static bool Matches(AutomationElement element, ElementWalkOptions options)
    {
        if (options.RoleFilter is { Length: > 0 } role &&
            !string.Equals(element.ControlType.ToString(), role, StringComparison.OrdinalIgnoreCase))
            return false;

        if (options.TitleContains is { Length: > 0 } title &&
            !TryGetName(element).Contains(title, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static ElementInfo ToElementInfo(string id, AutomationElement element) =>
        new(id, element.ControlType.ToString(), TryGetName(element), TryGetValue(element), ToFrame(element));

    private static string TryGetName(AutomationElement element)
    {
        try { return element.Name ?? ""; }
        catch { return ""; }
    }

    private static string? TryGetValue(AutomationElement element)
    {
        if (!element.Patterns.Value.IsSupported) return null;
        try { return element.Patterns.Value.Pattern.Value.Value; }
        catch { return null; }
    }

    private static Frame ToFrame(AutomationElement element)
    {
        var r = element.BoundingRectangle;
        return new Frame(r.X, r.Y, r.Width, r.Height);
    }

    public static Point LookupCenter(string elementId) => ToFrame(Require(elementId)).Center;

    public static bool TrySetValue(string elementId, string text)
    {
        var element = Require(elementId);
        if (!element.Patterns.Value.IsSupported) return false;
        try
        {
            element.Patterns.Value.Pattern.SetValue(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SetFocus(string elementId) => Require(elementId).Focus();

    private static AutomationElement Require(string elementId) =>
        ElementStore.Lookup(elementId) ?? throw new UiCtlException($"unknown element id \"{elementId}\"");
}
