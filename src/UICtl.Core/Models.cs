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

public sealed record WindowInfo(long WindowId, int Pid, string Title, Frame Frame);

public sealed record ResolvedWindow(long WindowId, int Pid);

public sealed record ElementInfo(string Id, string Role, string Title, string? Value, Frame Frame);

public sealed record AnnotatedElement(int Number, ElementInfo Element);

public sealed record ElementWalkResult(IReadOnlyList<ElementInfo> Elements, bool Truncated);

public sealed record ElementWalkOptions(string? RoleFilter = null, string? TitleContains = null, int MaxDepth = 25, int MaxElements = 500);

public sealed record PermissionsStatus(bool Elevated, bool? TargetProcessElevated);

public readonly record struct PixelColor(byte R, byte G, byte B, byte A);

/// <summary>One recognized line of text. <c>Confidence</c> is null on platforms whose OCR API doesn't report one (Windows.Media.Ocr does not).</summary>
public sealed record TextBlock(string Text, Frame Frame, double? Confidence);
