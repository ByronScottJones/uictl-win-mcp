using FlaUI.Core.AutomationElements;

namespace UICtl.Core;

/// <summary>
/// Maps synthetic element ids (handed out by EnumerateElements) back to the live
/// AutomationElement they came from, so a later click/type by id doesn't need to
/// re-walk the tree. Scoped per window; replaced wholesale each time that window's
/// elements are re-listed. The daemon serves one request at a time (see
/// ENGINEERING.md), so this needs no locking.
/// </summary>
internal static class ElementStore
{
    private static readonly Dictionary<string, AutomationElement> ElementsById = new();
    private static readonly Dictionary<long, int> CounterByWindow = new();

    public static void Reset(long windowId)
    {
        CounterByWindow[windowId] = 0;
        string prefix = $"{windowId}-";
        foreach (var key in ElementsById.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            ElementsById.Remove(key);
    }

    public static string Register(long windowId, AutomationElement element)
    {
        int next = CounterByWindow.GetValueOrDefault(windowId) + 1;
        CounterByWindow[windowId] = next;
        string id = $"{windowId}-{next}";
        ElementsById[id] = element;
        return id;
    }

    public static AutomationElement? Lookup(string id) => ElementsById.GetValueOrDefault(id);
}
