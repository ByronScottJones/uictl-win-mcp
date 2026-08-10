using System.Text.Json;
using UICtl.Core;

namespace UICtl.Ipc;

/// <summary>
/// The one switch every request goes through, regardless of front end (CLI or
/// MCP) - see ENGINEERING.md. Each case is a thin translator: parse JSON params
/// into typed Core calls, then shape the result into exactly the JSON
/// MCP_INTERFACE.md documents for that tool.
/// </summary>
public static class CommandDispatcher
{
    public static string Dispatch(string command, JsonElement @params)
    {
        try
        {
            return Envelope.Success(Execute(command, @params));
        }
        catch (Exception ex)
        {
            return Envelope.Failure(ex.Message);
        }
    }

    private static object Execute(string command, JsonElement p) => command switch
    {
        "permissions.status" => Permissions.Status(p.GetStringOrNull("app")),
        "permissions.request" => PermissionsRequest(p),

        "apps.list" => new Dictionary<string, object?> { ["apps"] = AppsAndWindows.ListApps(p.GetBoolOrDefault("all")) },
        "windows.list" => WindowsList(p),
        "activate" => AppsAndWindows.Activate(p.GetStringOrThrow("app"), p.GetLongOrNull("window")),

        "screenshot" => Screenshot(p),
        "elements" => Elements(p),

        "click" => Click(p),
        "move" => Move(p),
        "scroll" => Scroll(p),
        "type" => TypeText(p),
        "key" => Key(p),
        "waitFor" => WaitFor(p),

        "ocr" => Ocr(p),
        "pixel" => PixelSampler.ColorAt(Parsing.ParsePoint(p.GetStringOrThrow("at"))),

        "clipboard.get" => new Dictionary<string, object?> { ["text"] = Clipboard.Get() ?? "" },
        "clipboard.set" => ClipboardSet(p),

        _ => throw new UiCtlException($"unknown command \"{command}\""),
    };

    private static object PermissionsRequest(JsonElement p)
    {
        var status = Permissions.Status(p.GetStringOrNull("app"));
        return new Dictionary<string, object?>
        {
            ["elevated"] = status.Elevated,
            ["targetProcessElevated"] = status.TargetProcessElevated,
            ["requested"] = false,
        };
    }

    private static object WindowsList(JsonElement p)
    {
        int? pidFilter = p.GetStringOrNull("app") is { } app ? AppSelector.Resolve(app) : null;
        return new Dictionary<string, object?> { ["windows"] = AppsAndWindows.ListWindows(pidFilter) };
    }

    private static object Screenshot(JsonElement p)
    {
        bool annotate = p.GetBoolOrDefault("annotate");
        string outPath = p.GetStringOrNull("out") ?? DefaultScreenshotPath();
        string? roleFilter = p.GetStringOrNull("role");

        Capture capture;
        IReadOnlyList<AnnotatedElement>? annotated = null;

        if (p.GetIntOrNull("screen") is { } screenIndex)
        {
            capture = ScreenCapture.CaptureDisplay(screenIndex);
        }
        else
        {
            var resolved = WindowResolver.Resolve(p.GetLongOrNull("window"), p.GetStringOrNull("app"));
            capture = ScreenCapture.CaptureWindow(resolved.WindowId);
            if (annotate)
            {
                var windowElement = Automation.ResolveWindowElement(resolved.WindowId);
                var walk = Automation.EnumerateElements(resolved.WindowId, windowElement, new ElementWalkOptions(RoleFilter: roleFilter, MaxElements: 200));
                annotated = ScreenCapture.Annotate(capture.Image, capture.Origin, walk.Elements);
            }
        }

        using (capture)
        {
            ScreenCapture.SavePng(capture.Image, outPath);

            var data = new Dictionary<string, object?>
            {
                ["path"] = outPath,
                ["width"] = capture.Image.Width,
                ["height"] = capture.Image.Height,
            };
            if (annotated is not null)
                data["elements"] = annotated.Select(FlattenAnnotated).ToList();
            return data;
        }
    }

    private static Dictionary<string, object?> FlattenAnnotated(AnnotatedElement ae) => new()
    {
        ["number"] = ae.Number,
        ["id"] = ae.Element.Id,
        ["role"] = ae.Element.Role,
        ["title"] = ae.Element.Title,
        ["value"] = ae.Element.Value,
        ["frame"] = ae.Element.Frame,
    };

    private static string DefaultScreenshotPath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uictl", "screenshots");
        string stamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss.fffZ");
        return Path.Combine(dir, $"{stamp}.png");
    }

    private static object Elements(JsonElement p)
    {
        var resolved = WindowResolver.Resolve(p.GetLongOrNull("window"), p.GetStringOrNull("app"));
        var windowElement = Automation.ResolveWindowElement(resolved.WindowId);
        var options = new ElementWalkOptions(
            RoleFilter: p.GetStringOrNull("role"),
            TitleContains: p.GetStringOrNull("title"),
            MaxDepth: p.GetIntOrNull("maxDepth") ?? 25,
            MaxElements: p.GetIntOrNull("maxElements") ?? 500);
        var walk = Automation.EnumerateElements(resolved.WindowId, windowElement, options);

        var data = new Dictionary<string, object?>
        {
            ["windowId"] = resolved.WindowId,
            ["count"] = walk.Elements.Count,
            ["elements"] = walk.Elements,
        };
        if (walk.Truncated) data["truncated"] = true;
        return data;
    }

    private static object Click(JsonElement p)
    {
        Point point = p.GetStringOrNull("element") is { } elementId
            ? Automation.LookupCenter(elementId)
            : p.GetStringOrNull("at") is { } at
                ? Parsing.ParsePoint(at)
                : throw new UiCtlException("either \"at\" or \"element\" is required");

        var button = (p.GetStringOrNull("button") ?? "left").ToLowerInvariant() switch
        {
            "right" => MouseButton.Right,
            "center" => MouseButton.Center,
            _ => MouseButton.Left,
        };
        int count = p.GetBoolOrDefault("double") ? 2 : (p.GetIntOrNull("count") ?? 1);

        InputSynthesis.Click(point, button, count);
        return new Dictionary<string, object?> { ["clicked"] = point };
    }

    private static object Move(JsonElement p)
    {
        var point = Parsing.ParsePoint(p.GetStringOrThrow("at"));
        InputSynthesis.Move(point);
        return new Dictionary<string, object?> { ["moved"] = true };
    }

    private static object Scroll(JsonElement p)
    {
        var point = Parsing.ParsePoint(p.GetStringOrThrow("at"));
        InputSynthesis.Scroll(point, p.GetIntOrNull("dx") ?? 0, p.GetIntOrNull("dy") ?? 0);
        return new Dictionary<string, object?> { ["scrolled"] = true };
    }

    private static object TypeText(JsonElement p)
    {
        string text = p.GetStringOrThrow("text");
        string? elementId = p.GetStringOrNull("element");

        if (elementId is not null)
        {
            if (Automation.TrySetValue(elementId, text))
                return new Dictionary<string, object?> { ["method"] = "valuePattern", ["element"] = elementId };

            Automation.SetFocus(elementId);
            Thread.Sleep(50);
            InputSynthesis.TypeText(text);
            return new Dictionary<string, object?> { ["method"] = "synthesizedKeystrokes", ["element"] = elementId };
        }

        InputSynthesis.TypeText(text);
        return new Dictionary<string, object?> { ["method"] = "synthesizedKeystrokes" };
    }

    private static object Key(JsonElement p)
    {
        string combo = p.GetStringOrThrow("combo");
        InputSynthesis.SendKeyCombo(combo);
        return new Dictionary<string, object?> { ["sent"] = combo };
    }

    private static object WaitFor(JsonElement p)
    {
        var resolved = WindowResolver.Resolve(p.GetLongOrNull("window"), p.GetStringOrNull("app"));
        double timeoutSeconds = p.GetDoubleOrNull("timeout") ?? 10;
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var options = new ElementWalkOptions(RoleFilter: p.GetStringOrNull("role"), TitleContains: p.GetStringOrNull("title"), MaxElements: 1);

        while (DateTime.UtcNow < deadline)
        {
            var windowElement = Automation.ResolveWindowElement(resolved.WindowId);
            var walk = Automation.EnumerateElements(resolved.WindowId, windowElement, options);
            if (walk.Elements.Count > 0)
                return new Dictionary<string, object?> { ["found"] = true, ["element"] = walk.Elements[0] };
            Thread.Sleep(200);
        }
        return new Dictionary<string, object?> { ["found"] = false };
    }

    private static object Ocr(JsonElement p)
    {
        Frame? region = p.GetStringOrNull("region") is { } r ? Parsing.ParseFrame(r) : null;

        IReadOnlyList<TextBlock> blocks;
        if (p.GetStringOrNull("image") is { } imagePath)
        {
            blocks = OCR.RecognizeImageFile(imagePath, region);
        }
        else
        {
            var resolved = WindowResolver.Resolve(p.GetLongOrNull("window"), p.GetStringOrNull("app"));
            using var capture = ScreenCapture.CaptureWindow(resolved.WindowId);
            blocks = OCR.RecognizeCapture(capture, region);
        }

        return new Dictionary<string, object?> { ["textBlocks"] = blocks };
    }

    private static object ClipboardSet(JsonElement p)
    {
        Clipboard.Set(p.GetStringOrThrow("text"));
        return new Dictionary<string, object?> { ["set"] = true };
    }
}
