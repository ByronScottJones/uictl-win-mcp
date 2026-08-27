using System.Net.Http.Headers;
using System.Text.Json;

namespace UICtl.Core;

public sealed record GitHubIssueSummary(int Number, string Title, string Url, string State);

/// <summary>
/// Every failure mode fetching from GitHub (missing token, rate limit,
/// network error) surfaces as this, so callers can treat "couldn't check" as
/// "skip the check" rather than blocking submission on it.
/// </summary>
public sealed class GitHubIssuesException(string message) : Exception(message);

/// <summary>
/// Fetches a repo's issues from the public GitHub REST API, to check a draft
/// feedback entry against before submitting it. Unauthenticated requests
/// work fine against public repos (just a much lower rate limit); a private
/// repo needs a token - see <see cref="GitHubToken"/>. Mirrors macOS's
/// GitHubIssues.swift.
/// </summary>
public static class GitHubIssues
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static IReadOnlyList<GitHubIssueSummary> FetchAll(string repo, string? token)
    {
        var all = new List<GitHubIssueSummary>();

        // A generous but finite cap - this is a duplicate-title scan, not a
        // full mirror; 1000 issues is far more than that needs.
        for (int page = 1; page <= 10; page++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{repo}/issues?state=all&per_page=100&page={page}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("uictl", "0.1.0"));
            if (token is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response;
            try
            {
                response = Client.Send(request);
            }
            catch (Exception ex)
            {
                throw new GitHubIssuesException($"network error contacting GitHub: {ex.Message}");
            }

            using (response)
            {
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    string hint = token is null
                        ? "this repo may be private - set GITHUB_TOKEN, authenticate `gh`, or pass --token"
                        : "check that the token has access to this repo";
                    throw new GitHubIssuesException($"GitHub returned HTTP {(int)response.StatusCode} for {repo} ({hint})");
                }

                using var stream = response.Content.ReadAsStream();
                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(stream);
                }
                catch
                {
                    throw new GitHubIssuesException("failed to parse GitHub's response");
                }

                using (doc)
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Array)
                        throw new GitHubIssuesException("failed to parse GitHub's response");

                    int count = 0;
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        count++;
                        // The issues endpoint also returns pull requests.
                        if (item.TryGetProperty("pull_request", out _)) continue;
                        if (!item.TryGetProperty("number", out var numberProp) ||
                            !item.TryGetProperty("title", out var titleProp) ||
                            !item.TryGetProperty("html_url", out var urlProp) ||
                            !item.TryGetProperty("state", out var stateProp))
                            continue;

                        all.Add(new GitHubIssueSummary(numberProp.GetInt32(), titleProp.GetString() ?? "", urlProp.GetString() ?? "", stateProp.GetString() ?? ""));
                    }
                    if (count < 100) break;
                }
            }
        }

        return all;
    }

    /// <summary>
    /// A deliberately simple, transparent heuristic - case-insensitive
    /// equality or substring containment either direction - meant to catch
    /// obvious re-reports, not near-miss wording. Not fuzzy matching.
    /// </summary>
    public static IReadOnlyList<GitHubIssueSummary> FindDuplicates(string title, IReadOnlyList<GitHubIssueSummary> issues)
    {
        string normalized = title.Trim().ToLowerInvariant();
        if (normalized.Length == 0) return [];

        return issues.Where(issue =>
        {
            string other = issue.Title.Trim().ToLowerInvariant();
            // An empty issue title would otherwise match every draft, since
            // normalized.Contains("") is always true.
            if (other.Length == 0) return false;
            return other == normalized || other.Contains(normalized) || normalized.Contains(other);
        }).ToList();
    }
}
