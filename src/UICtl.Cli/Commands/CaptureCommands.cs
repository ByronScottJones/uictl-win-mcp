using System.CommandLine;

namespace UICtl.Cli.Commands;

internal static class CaptureCommands
{
    public static Command Screenshot()
    {
        var window = new Option<long?>("--window") { Description = "Capture this window id (from `windows`)." };
        var app = new Option<string?>("--app") { Description = "Capture this app's frontmost on-screen window (name substring, package family name, or pid)." };
        var screen = new Option<int?>("--screen") { Description = "Capture this display by index (from 0), instead of a window." };
        var outPath = new Option<string?>("--out") { Description = "Output PNG path. Defaults under %LOCALAPPDATA%\\uictl\\screenshots\\." };
        var annotate = new Option<bool>("--annotate") { Description = "Overlay numbered boxes on every UI Automation element and return a legend." };
        var role = new Option<string?>("--role") { Description = "When annotating, only include elements with this ControlType (e.g. Button)." };

        var cmd = new Command("screenshot", "Capture a window, an app's frontmost window, or a display.");
        cmd.Add(window);
        cmd.Add(app);
        cmd.Add(screen);
        cmd.Add(outPath);
        cmd.Add(annotate);
        cmd.Add(role);
        cmd.SetAction(async (pr, ct) =>
        {
            var args = new Dictionary<string, object?>();
            if (pr.GetValue(window) is { } w) args["window"] = w;
            if (pr.GetValue(app) is { } a) args["app"] = a;
            if (pr.GetValue(screen) is { } s) args["screen"] = s;
            if (pr.GetValue(outPath) is { } o) args["out"] = o;
            if (pr.GetValue(annotate)) args["annotate"] = true;
            if (pr.GetValue(role) is { } r) args["role"] = r;
            return await CliRunner.RunAsync("screenshot", args, ct);
        });
        return cmd;
    }

    public static Command Elements()
    {
        var window = new Option<long?>("--window") { Description = "Window id (from `windows`). Either this or --app is required." };
        var app = new Option<string?>("--app") { Description = "App to inspect's frontmost window (name substring, package family name, or pid)." };
        var role = new Option<string?>("--role") { Description = "Only include elements with this ControlType (e.g. Button, Edit)." };
        var title = new Option<string?>("--title") { Description = "Only include elements whose name contains this substring." };
        var maxDepth = new Option<int?>("--maxDepth") { Description = "Maximum tree depth to walk." };
        var maxElements = new Option<int?>("--maxElements") { Description = "Stop after this many matching elements." };

        var cmd = new Command("elements", "Walk a window's UI Automation tree.");
        cmd.Add(window);
        cmd.Add(app);
        cmd.Add(role);
        cmd.Add(title);
        cmd.Add(maxDepth);
        cmd.Add(maxElements);
        cmd.SetAction(async (pr, ct) =>
        {
            var args = new Dictionary<string, object?>();
            if (pr.GetValue(window) is { } w) args["window"] = w;
            if (pr.GetValue(app) is { } a) args["app"] = a;
            if (pr.GetValue(role) is { } r) args["role"] = r;
            if (pr.GetValue(title) is { } t) args["title"] = t;
            if (pr.GetValue(maxDepth) is { } md) args["maxDepth"] = md;
            if (pr.GetValue(maxElements) is { } me) args["maxElements"] = me;
            return await CliRunner.RunAsync("elements", args, ct);
        });
        return cmd;
    }

    public static Command Ocr()
    {
        var image = new Option<string?>("--image") { Description = "Path to a PNG file to run OCR on, instead of capturing a window." };
        var window = new Option<long?>("--window") { Description = "Window id to capture and OCR (from `windows`)." };
        var app = new Option<string?>("--app") { Description = "App whose frontmost window to capture and OCR." };
        var region = new Option<string?>("--region") { Description = "Restrict OCR to this region, in the same coordinate space as `elements` frames: \"x,y,w,h\"." };

        var cmd = new Command("ocr", "Recognize text in an image file or a window, optionally restricted to a region.");
        cmd.Add(image);
        cmd.Add(window);
        cmd.Add(app);
        cmd.Add(region);
        cmd.SetAction(async (pr, ct) =>
        {
            var args = new Dictionary<string, object?>();
            if (pr.GetValue(image) is { } i) args["image"] = i;
            if (pr.GetValue(window) is { } w) args["window"] = w;
            if (pr.GetValue(app) is { } a) args["app"] = a;
            if (pr.GetValue(region) is { } r) args["region"] = r;
            return await CliRunner.RunAsync("ocr", args, ct);
        });
        return cmd;
    }

    public static Command Pixel()
    {
        var at = new Option<string>("--at") { Description = "Screen point to sample: \"x,y\".", Required = true };

        var cmd = new Command("pixel", "Sample the color of a single screen pixel.");
        cmd.Add(at);
        cmd.SetAction(async (pr, ct) => await CliRunner.RunAsync("pixel", new() { ["at"] = pr.GetValue(at) }, ct));
        return cmd;
    }
}
