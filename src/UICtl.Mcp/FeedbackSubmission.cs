using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UICtl.Core;
using UICtl.Ipc;

namespace UICtl.Mcp;

/// <summary>
/// Implements <c>uictl_feedback_submit</c>'s elicitation flow. Lives outside
/// <see cref="ToolDefinitions"/>'s generic forward-to-the-daemon path because
/// it needs the live <see cref="McpServer"/> for *this* request - elicitation
/// only exists between this MCP session and its connected client, and the
/// daemon (talking only over a named pipe) has no access to that. Mirrors
/// macOS's <c>MCP/FeedbackSubmission.swift</c>; the C# SDK's native form/url
/// elicitation support (<see cref="McpServer.ElicitAsync"/>) means this is
/// considerably shorter than the Swift version, which had to hand-roll
/// URL-mode elicitation and a timeout race the vendored Swift SDK doesn't
/// provide - here a plain <see cref="CancellationTokenSource"/> does the job.
/// </summary>
internal static class FeedbackSubmission
{
    private const string DefaultRepo = "byronjones-elsevier/uictl-win-mcp";

    /// <summary>
    /// How long to wait for the connected client to respond to an
    /// elicitation before giving up and falling back to a direct open - an
    /// unsupported or unresponsive client would otherwise hang this tool
    /// call forever.
    /// </summary>
    private static readonly TimeSpan ElicitationTimeout = TimeSpan.FromSeconds(120);

    public static async Task<CallToolResult> HandleAsync(McpServer server, IDictionary<string, JsonElement>? arguments, CancellationToken ct)
    {
        try
        {
            var args = arguments ?? new Dictionary<string, JsonElement>();
            if (!args.TryGetValue("id", out var idElement))
                return ErrorResult("\"id\" is required");
            int id = idElement.GetInt32();
            string repo = args.TryGetValue("repo", out var repoElement) ? repoElement.GetString() ?? DefaultRepo : DefaultRepo;
            string? token = args.TryGetValue("token", out var tokenElement) ? tokenElement.GetString() : null;

            var fetched = SendDaemon("feedback.get", new Dictionary<string, object?> { ["id"] = id });
            if (!fetched.Ok) return ErrorResult(fetched.Error ?? $"feedback entry {id} not found");
            string originalTitle = fetched.Data!.Value.GetProperty("title").GetString() ?? "";
            string originalBody = fetched.Data!.Value.GetProperty("body").GetString() ?? "";
            string title = originalTitle;
            string body = originalBody;

            // Check for an existing GitHub issue before bothering the human
            // with a review prompt at all - if this already exists there,
            // there's nothing to review, just discard the local draft.
            var dupArgs = new Dictionary<string, object?> { ["id"] = id, ["repo"] = repo };
            if (token is not null) dupArgs["token"] = token;
            var duplicates = SendDaemon("feedback.checkDuplicates", dupArgs);
            if (duplicates.Ok &&
                duplicates.Data!.Value.TryGetProperty("duplicates", out var duplicatesArray) &&
                duplicatesArray.ValueKind == JsonValueKind.Array &&
                duplicatesArray.GetArrayLength() > 0)
            {
                SendDaemon("feedback.delete", new Dictionary<string, object?> { ["id"] = id });
                return SuccessResult(new Dictionary<string, object?>
                {
                    ["submitted"] = false,
                    ["duplicate"] = true,
                    ["deletedLocally"] = true,
                    ["matchedIssue"] = duplicatesArray[0],
                });
            }

            // Step 1: form-mode elicitation - let the human review (and
            // tweak) the content before anything about it leaves this
            // machine, since it was an agent asking to send it, not them.
            ElicitResult review;
            try
            {
                review = await ElicitWithTimeout(server, new ElicitRequestParams
                {
                    Message = $"An AI agent wants to submit this feedback to {repo}'s GitHub Issues. Review it (edit either field if you like) before it's sent anywhere:",
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                        {
                            ["title"] = new ElicitRequestParams.StringSchema { Title = "Title", Default = originalTitle },
                            ["body"] = new ElicitRequestParams.StringSchema { Title = "Description", Default = originalBody },
                        },
                        Required = ["title", "body"],
                    },
                }, ct);
            }
            catch (Exception ex)
            {
                // Client likely doesn't support elicitation (or timed out) -
                // fall back to the same non-interactive path the CLI uses,
                // rather than failing outright.
                return FallbackSubmit(id, repo, token, ex.Message);
            }

            if (!review.IsAccepted)
                return SuccessResult(new Dictionary<string, object?> { ["submitted"] = false, ["reason"] = $"review {review.Action}ed" });

            if (review.Content?.TryGetValue("title", out var editedTitle) == true && editedTitle.GetString() is { Length: > 0 } newTitle) title = newTitle;
            if (review.Content?.TryGetValue("body", out var editedBody) == true && editedBody.GetString() is { Length: > 0 } newBody) body = newBody;
            if (title != originalTitle || body != originalBody)
                SendDaemon("feedback.update", new Dictionary<string, object?> { ["id"] = id, ["title"] = title, ["body"] = body });

            var built = SendDaemon("feedback.buildUrl", new Dictionary<string, object?> { ["id"] = id, ["repo"] = repo });
            if (!built.Ok || !built.Data!.Value.TryGetProperty("url", out var urlProp))
                return ErrorResult(built.Error ?? "failed to build a submission URL");
            string urlString = urlProp.GetString()!;

            // Step 2: URL-mode elicitation - hand the prepopulated page to
            // the client rather than the daemon silently opening a browser
            // tab on the agent's say-so.
            try
            {
                await ElicitWithTimeout(server, new ElicitRequestParams
                {
                    Mode = "url",
                    Message = "Opening GitHub with this feedback pre-filled - review and click \"Create\" there to finish.",
                    Url = urlString,
                    ElicitationId = Guid.NewGuid().ToString(),
                }, ct);

                var marked = SendDaemon("feedback.markSubmitted", new Dictionary<string, object?> { ["id"] = id, ["url"] = urlString });
                return SuccessResult(new Dictionary<string, object?>
                {
                    ["submitted"] = true,
                    ["url"] = urlString,
                    ["entry"] = marked.Data,
                });
            }
            catch (Exception ex)
            {
                return FallbackSubmit(id, repo, token, ex.Message);
            }
        }
        catch (Exception ex)
        {
            return ErrorResult(ex.Message);
        }
    }

    private static async Task<ElicitResult> ElicitWithTimeout(McpServer server, ElicitRequestParams requestParams, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(ElicitationTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
        return await server.ElicitAsync(requestParams, linked.Token);
    }

    /// <summary>
    /// The client couldn't (or didn't) handle one of the elicitations - open
    /// the URL directly on this machine instead of failing outright, same as
    /// the CLI's `feedback submit`. This still gets the feedback as far as a
    /// human's browser; it just skips the review round trip. (`feedback.submit`
    /// re-runs its own duplicate check regardless, so this can still
    /// legitimately come back as "deleted as a duplicate" rather than
    /// "submitted" - don't override whatever it reports.)
    /// </summary>
    private static CallToolResult FallbackSubmit(int id, string repo, string? token, string reason)
    {
        var args = new Dictionary<string, object?> { ["id"] = id, ["repo"] = repo };
        if (token is not null) args["token"] = token;
        var submitted = SendDaemon("feedback.submit", args);
        if (!submitted.Ok || submitted.Data is not { } data)
            return ErrorResult(submitted.Error ?? reason);

        var result = JsonSerializer.Deserialize<Dictionary<string, object?>>(data.GetRawText()) ?? [];
        result.TryAdd("submitted", true);
        result["elicitationFallback"] = reason;
        return SuccessResult(result);
    }

    private readonly record struct DaemonResult(bool Ok, JsonElement? Data, string? Error);

    private static DaemonResult SendDaemon(string command, Dictionary<string, object?> args)
    {
        var paramsElement = JsonSerializer.SerializeToElement(args);
        using var doc = JsonDocument.Parse(DaemonClient.Send(command, paramsElement));
        var root = doc.RootElement.Clone();
        bool ok = root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
        JsonElement? data = ok && root.TryGetProperty("data", out var dataProp) ? dataProp : null;
        string? error = !ok && root.TryGetProperty("error", out var errorProp) ? errorProp.GetString() : null;
        return new DaemonResult(ok, data, error);
    }

    private static CallToolResult SuccessResult(object data) => new()
    {
        Content = [new TextContentBlock { Text = Envelope.Success(data) }],
        IsError = false,
    };

    private static CallToolResult ErrorResult(string message) => new()
    {
        Content = [new TextContentBlock { Text = Envelope.Failure(message) }],
        IsError = true,
    };
}
