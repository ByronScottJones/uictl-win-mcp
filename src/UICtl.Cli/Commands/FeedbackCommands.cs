using System.CommandLine;

namespace UICtl.Cli.Commands;

internal static class FeedbackCommands
{
    public static Command Feedback()
    {
        var cmd = new Command(
            "feedback",
            "Draft, review, and submit feedback (issues/errors/recommendations) about uictl itself. " +
            "Stored locally first (%LOCALAPPDATA%\\uictl\\feedback.json) so it can be drafted, listed, and " +
            "edited before anything leaves this machine. `submit` opens a pre-filled GitHub \"new issue\" page - " +
            "it does not create the issue itself; you still need to review and click \"Create\" there.");
        cmd.Add(Create());
        cmd.Add(List());
        cmd.Add(Get());
        cmd.Add(Update());
        cmd.Add(Delete());
        cmd.Add(CheckDuplicates());
        cmd.Add(Submit());
        return cmd;
    }

    private static Command Create()
    {
        var category = new Option<string>("--category") { Description = "One of: issue, error, recommendation.", Required = true };
        var title = new Option<string>("--title") { Description = "Short summary - becomes the GitHub issue title.", Required = true };
        var body = new Option<string>("--body") { Description = "Full description - becomes the GitHub issue body.", Required = true };

        var cmd = new Command("create", "Draft a new local feedback entry.");
        cmd.Add(category);
        cmd.Add(title);
        cmd.Add(body);
        cmd.SetAction(async (pr, ct) => await CliRunner.RunAsync("feedback.create", new Dictionary<string, object?>
        {
            ["category"] = pr.GetValue(category),
            ["title"] = pr.GetValue(title),
            ["body"] = pr.GetValue(body),
        }, ct));
        return cmd;
    }

    private static Command List()
    {
        var cmd = new Command("list", "List all local feedback entries.");
        cmd.SetAction(async (_, ct) => await CliRunner.RunAsync("feedback.list", new Dictionary<string, object?>(), ct));
        return cmd;
    }

    private static Command Get()
    {
        var id = new Argument<int>("id") { Description = "Feedback entry id (from `feedback list`)." };

        var cmd = new Command("get", "Show one feedback entry in full.");
        cmd.Add(id);
        cmd.SetAction(async (pr, ct) => await CliRunner.RunAsync("feedback.get", new Dictionary<string, object?> { ["id"] = pr.GetValue(id) }, ct));
        return cmd;
    }

    private static Command Update()
    {
        var id = new Argument<int>("id") { Description = "Feedback entry id (from `feedback list`)." };
        var category = new Option<string?>("--category") { Description = "One of: issue, error, recommendation." };
        var title = new Option<string?>("--title") { Description = "New title." };
        var body = new Option<string?>("--body") { Description = "New body." };

        var cmd = new Command("update", "Edit a local feedback entry.");
        cmd.Add(id);
        cmd.Add(category);
        cmd.Add(title);
        cmd.Add(body);
        cmd.SetAction(async (pr, ct) =>
        {
            var args = new Dictionary<string, object?> { ["id"] = pr.GetValue(id) };
            if (pr.GetValue(category) is { } c) args["category"] = c;
            if (pr.GetValue(title) is { } t) args["title"] = t;
            if (pr.GetValue(body) is { } b) args["body"] = b;
            return await CliRunner.RunAsync("feedback.update", args, ct);
        });
        return cmd;
    }

    private static Command Delete()
    {
        var id = new Argument<int>("id") { Description = "Feedback entry id (from `feedback list`)." };

        var cmd = new Command("delete", "Delete a local feedback entry.");
        cmd.Add(id);
        cmd.SetAction(async (pr, ct) => await CliRunner.RunAsync("feedback.delete", new Dictionary<string, object?> { ["id"] = pr.GetValue(id) }, ct));
        return cmd;
    }

    private static Command CheckDuplicates()
    {
        var id = new Argument<int>("id") { Description = "Feedback entry id (from `feedback list`)." };
        var repo = new Option<string?>("--repo") { Description = "GitHub repo to check against, as \"owner/repo\". Defaults to uictl's own repo." };
        var token = new Option<string?>("--token") { Description = "GitHub token, if the repo needs one. Falls back to $GITHUB_TOKEN, then `gh auth token`." };

        var cmd = new Command(
            "check-duplicates",
            "Check a local entry's title against existing GitHub issues, without submitting anything. " +
            "Needs a GitHub token if the repo is private: pass --token, set GITHUB_TOKEN, or have `gh` already " +
            "authenticated - otherwise this just reports that it couldn't check. `submit` runs this same check " +
            "automatically before opening anything.");
        cmd.Add(id);
        cmd.Add(repo);
        cmd.Add(token);
        cmd.SetAction(async (pr, ct) =>
        {
            var args = new Dictionary<string, object?> { ["id"] = pr.GetValue(id) };
            if (pr.GetValue(repo) is { } r) args["repo"] = r;
            if (pr.GetValue(token) is { } t) args["token"] = t;
            return await CliRunner.RunAsync("feedback.checkDuplicates", args, ct);
        });
        return cmd;
    }

    private static Command Submit()
    {
        var id = new Argument<int>("id") { Description = "Feedback entry id (from `feedback list`)." };
        var repo = new Option<string?>("--repo") { Description = "GitHub repo to submit to, as \"owner/repo\". Defaults to uictl's own repo." };
        var token = new Option<string?>("--token") { Description = "GitHub token, for the duplicate check against a private repo. Falls back to $GITHUB_TOKEN, then `gh auth token`." };

        var cmd = new Command(
            "submit",
            "Check for an existing duplicate, then open GitHub's new-issue page with this entry's title/body pre-filled. " +
            "If a likely duplicate is found, the local entry is deleted and nothing is opened. Otherwise opens the URL " +
            "directly in your default browser - running this command is treated as your confirmation. (The MCP tool " +
            "uictl_feedback_submit instead asks you to review it first via MCP elicitation, since an agent may call it " +
            "without you having typed anything yourself.)");
        cmd.Add(id);
        cmd.Add(repo);
        cmd.Add(token);
        cmd.SetAction(async (pr, ct) =>
        {
            var args = new Dictionary<string, object?> { ["id"] = pr.GetValue(id) };
            if (pr.GetValue(repo) is { } r) args["repo"] = r;
            if (pr.GetValue(token) is { } t) args["token"] = t;
            return await CliRunner.RunAsync("feedback.submit", args, ct);
        });
        return cmd;
    }
}
