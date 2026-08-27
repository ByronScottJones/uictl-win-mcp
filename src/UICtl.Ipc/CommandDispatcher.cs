using System.Diagnostics;
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
    /// <summary>Every CLI/MCP call funnels through here, so timing/logging it once here - rather than in each case - covers all of them uniformly. Mirrors macOS's CommandDispatcher.swift dispatch/dispatchInner split.</summary>
    public static string Dispatch(string command, JsonElement @params)
    {
        var stopwatch = Stopwatch.StartNew();
        string response = UICtlGate.CommandsEnabled
            ? DispatchInner(command, @params)
            : Envelope.Failure("commands are disabled - toggle \"Commands enabled\" in the uictl Activity Log window (uictl log show) back on");
        double durationMs = stopwatch.Elapsed.TotalMilliseconds;
        ActivityLog.Record(command, @params, response, durationMs);
        return response;
    }

    private static string DispatchInner(string command, JsonElement @params)
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
        "displays.list" => new Dictionary<string, object?> { ["displays"] = Displays.List() },
        "activate" => AppsAndWindows.Activate(p.GetStringOrThrow("app"), p.GetLongOrNull("window")),

        "focus.hold" => FocusHold.Hold(p.GetLongOrNull("window"), p.GetStringOrNull("app")),
        "focus.release" => FocusHold.Release(),
        "focus.status" => FocusHold.Status(),

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

        "feedback.create" => FeedbackStore.Create(p.GetStringOrThrow("category"), p.GetStringOrThrow("title"), p.GetStringOrThrow("body")),
        "feedback.list" => FeedbackStore.List(),
        "feedback.get" => FeedbackStore.Get(RequireId(p)),
        "feedback.update" => FeedbackStore.Update(RequireId(p), p.GetStringOrNull("category"), p.GetStringOrNull("title"), p.GetStringOrNull("body")),
        "feedback.delete" => FeedbackDelete(p),
        "feedback.buildUrl" => FeedbackBuildUrl(p),
        "feedback.markSubmitted" => FeedbackStore.MarkSubmitted(RequireId(p), p.GetStringOrThrow("url")),
        "feedback.checkDuplicates" => FeedbackCheckDuplicates(p),
        "feedback.submit" => FeedbackSubmit(p),

        "log.show" => LogShow(),
        "log.export" => LogExport(p),

        _ => throw new UiCtlException($"unknown command \"{command}\""),
    };

    /// <summary>Feedback about uictl itself defaults to filing against this repo unless a caller names another.</summary>
    private const string DefaultFeedbackRepo = "byronjones-elsevier/uictl-win-mcp";

    private static int RequireId(JsonElement p) => p.GetIntOrNull("id") ?? throw new UiCtlException("\"id\" is required");

    private static object PermissionsRequest(JsonElement p)
    {
        var status = Permissions.Status(p.GetStringOrNull("app"));
        return new Dictionary<string, object?>
        {
            ["elevated"] = status.Elevated,
            ["targetProcessElevated"] = status.TargetProcessElevated,
            ["interactive"] = status.Interactive,
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
        string focusHold = FocusHold.EnsureFocused();

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
        var data = new Dictionary<string, object?> { ["clicked"] = point };
        return WithFocusHold(data, focusHold);
    }

    private static object Move(JsonElement p)
    {
        string focusHold = FocusHold.EnsureFocused();
        var point = Parsing.ParsePoint(p.GetStringOrThrow("at"));
        InputSynthesis.Move(point);
        return WithFocusHold(new Dictionary<string, object?> { ["moved"] = true }, focusHold);
    }

    private static object Scroll(JsonElement p)
    {
        string focusHold = FocusHold.EnsureFocused();
        var point = Parsing.ParsePoint(p.GetStringOrThrow("at"));
        InputSynthesis.Scroll(point, p.GetIntOrNull("dx") ?? 0, p.GetIntOrNull("dy") ?? 0);
        return WithFocusHold(new Dictionary<string, object?> { ["scrolled"] = true }, focusHold);
    }

    private static object TypeText(JsonElement p)
    {
        string focusHold = FocusHold.EnsureFocused();
        string text = p.GetStringOrThrow("text");
        string? elementId = p.GetStringOrNull("element");

        if (elementId is not null)
        {
            if (Automation.TrySetValue(elementId, text))
                return WithFocusHold(new Dictionary<string, object?> { ["method"] = "valuePattern", ["element"] = elementId }, focusHold);

            Automation.SetFocus(elementId);
            Thread.Sleep(50);
            InputSynthesis.TypeText(text);
            return WithFocusHold(new Dictionary<string, object?> { ["method"] = "synthesizedKeystrokes", ["element"] = elementId }, focusHold);
        }

        InputSynthesis.TypeText(text);
        return WithFocusHold(new Dictionary<string, object?> { ["method"] = "synthesizedKeystrokes" }, focusHold);
    }

    private static object Key(JsonElement p)
    {
        string focusHold = FocusHold.EnsureFocused();
        string combo = p.GetStringOrThrow("combo");
        InputSynthesis.SendKeyCombo(combo);
        return WithFocusHold(new Dictionary<string, object?> { ["sent"] = combo }, focusHold);
    }

    /// <summary>Attaches "focusHold" only while a hold is actually active - EnsureFocused returns "notHeld" as a no-op when nothing is held, and that value is never surfaced (mirrors macOS's CommandDispatcher.swift).</summary>
    private static Dictionary<string, object?> WithFocusHold(Dictionary<string, object?> data, string focusHold)
    {
        if (focusHold != "notHeld")
            data["focusHold"] = focusHold;
        return data;
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

    private static object FeedbackDelete(JsonElement p)
    {
        int id = RequireId(p);
        FeedbackStore.Delete(id);
        return new Dictionary<string, object?> { ["deleted"] = id };
    }

    private static object FeedbackBuildUrl(JsonElement p)
    {
        var entry = FeedbackStore.Get(RequireId(p));
        string repo = p.GetStringOrNull("repo") ?? DefaultFeedbackRepo;
        return new Dictionary<string, object?> { ["url"] = FeedbackStore.SubmissionUrl(entry, repo).AbsoluteUri };
    }

    private static object FeedbackCheckDuplicates(JsonElement p)
    {
        var entry = FeedbackStore.Get(RequireId(p));
        string repo = p.GetStringOrNull("repo") ?? DefaultFeedbackRepo;
        string? token = GitHubToken.Resolve(p.GetStringOrNull("token"));

        try
        {
            var issues = GitHubIssues.FetchAll(repo, token);
            var duplicates = GitHubIssues.FindDuplicates(entry.Title, issues);
            return new Dictionary<string, object?>
            {
                ["checked"] = true,
                ["usedToken"] = token is not null,
                ["duplicates"] = duplicates.Select(DuplicateDict).ToList(),
            };
        }
        catch (GitHubIssuesException ex)
        {
            return new Dictionary<string, object?> { ["checked"] = false, ["reason"] = ex.Message, ["duplicates"] = Array.Empty<object>() };
        }
    }

    /// <summary>
    /// Checks for an existing GitHub issue before opening anything - a match
    /// means this draft is a re-report, not new feedback, so it's discarded
    /// locally rather than submitted. If the check itself can't run (no token
    /// against a private repo, network error, ...), that's not treated as a
    /// failure - submission just proceeds without it. Mirrors macOS's
    /// CommandDispatcher.swift "feedback.submit" case.
    /// </summary>
    private static object FeedbackSubmit(JsonElement p)
    {
        int id = RequireId(p);
        var entry = FeedbackStore.Get(id);
        string repo = p.GetStringOrNull("repo") ?? DefaultFeedbackRepo;
        string? token = GitHubToken.Resolve(p.GetStringOrNull("token"));

        string duplicateCheckNote = "not attempted";
        try
        {
            var issues = GitHubIssues.FetchAll(repo, token);
            duplicateCheckNote = "ok, no duplicate found";
            var duplicate = GitHubIssues.FindDuplicates(entry.Title, issues).FirstOrDefault();
            if (duplicate is not null)
            {
                FeedbackStore.Delete(id);
                return new Dictionary<string, object?>
                {
                    ["submitted"] = false,
                    ["duplicate"] = true,
                    ["deletedLocally"] = true,
                    ["matchedIssue"] = DuplicateDict(duplicate),
                };
            }
        }
        catch (GitHubIssuesException ex)
        {
            duplicateCheckNote = $"skipped: {ex.Message}";
        }

        var url = FeedbackStore.SubmissionUrl(entry, repo);
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        var updated = FeedbackStore.MarkSubmitted(id, url.AbsoluteUri);
        return new Dictionary<string, object?>
        {
            ["url"] = url.AbsoluteUri,
            ["opened"] = true,
            ["duplicateCheck"] = duplicateCheckNote,
            ["entry"] = updated,
        };
    }

    private static Dictionary<string, object?> DuplicateDict(GitHubIssueSummary issue) => new()
    {
        ["number"] = issue.Number,
        ["title"] = issue.Title,
        ["url"] = issue.Url,
        ["state"] = issue.State,
    };

    /// <summary>
    /// Ipc has no reference to the WPF GUI project by design (ActivityLog
    /// stays UI-framework agnostic - see its doc comment), so this goes
    /// through the hook the GUI layer installs at daemon startup rather than
    /// calling a window directly. A no-op (still returns shown:true, matching
    /// macOS's CommandDispatcher.swift which does the same unconditionally)
    /// if nothing has installed the hook - e.g. under test, or if the GUI
    /// layer somehow failed to start.
    /// </summary>
    private static object LogShow()
    {
        ActivityLog.ShowWindow?.Invoke();
        return new Dictionary<string, object?> { ["shown"] = true };
    }

    private static object LogExport(JsonElement p)
    {
        string path = p.GetStringOrNull("out") ?? DefaultLogExportPath();
        ActivityLog.ExportJson(path);
        return new Dictionary<string, object?> { ["path"] = path };
    }

    private static string DefaultLogExportPath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uictl", "exports");
        string stamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss.fffZ");
        return Path.Combine(dir, $"uictl-activity-{stamp}.json");
    }
}
