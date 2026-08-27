namespace UICtl.Core;

/// <summary>Screen coordinate: top-left origin, y increasing downward, physical pixels. See MCP_INTERFACE.md.</summary>
public readonly record struct Point(double X, double Y);

/// <summary>Bounding rectangle in the same coordinate space as <see cref="Point"/>.</summary>
public readonly record struct Frame(double X, double Y, double W, double H)
{
    public Point Center => new(X + W / 2, Y + H / 2);
}

public enum MouseButton { Left, Right, Center }

public sealed record AppInfo(int Pid, string Name, string BundleId);

public sealed record WindowInfo(long WindowId, int Pid, string Title, Frame Frame, long? DisplayId);

/// <summary>
/// One monitor. <c>Index</c> matches what `screenshot --screen &lt;index&gt;`
/// expects; <c>DisplayId</c> is the raw HMONITOR value (mirrors macOS's
/// CGDirectDisplayID-as-displayId convention - see MCP_INTERFACE.md's
/// "Window id" note on platform-specific id widths). <c>Scale</c> is the
/// monitor's effective DPI divided by 96, standing in for macOS's
/// pointPixelScale.
/// </summary>
public sealed record DisplayInfo(int Index, long DisplayId, Frame Frame, bool IsMain, double Scale);

public sealed record ResolvedWindow(long WindowId, int Pid);

public sealed record ElementInfo(string Id, string Role, string Title, string? Value, Frame Frame);

public sealed record AnnotatedElement(int Number, ElementInfo Element);

public sealed record ElementWalkResult(IReadOnlyList<ElementInfo> Elements, bool Truncated);

public sealed record ElementWalkOptions(string? RoleFilter = null, string? TitleContains = null, int MaxDepth = 25, int MaxElements = 500);

public sealed record PermissionsStatus(bool Elevated, bool? TargetProcessElevated, bool Interactive);

public readonly record struct PixelColor(byte R, byte G, byte B, byte A);

/// <summary>One recognized line of text. <c>Confidence</c> is null on platforms whose OCR API doesn't report one (Windows.Media.Ocr does not).</summary>
public sealed record TextBlock(string Text, Frame Frame, double? Confidence);
