using UICtl.Core;

namespace UICtl.Core.Tests;

/// <summary>
/// Pure-function coverage of the duplicate-title heuristic - deliberately
/// not testing GitHubIssues.FetchAll here, since that's a real network call
/// against api.github.com (exercised live instead - see TESTING.md's Phase 3
/// update note).
/// </summary>
public class GitHubIssuesTests
{
    private static GitHubIssueSummary Issue(string title) => new(1, title, "https://example.com/1", "open");

    [Fact]
    public void FindDuplicates_ExactCaseInsensitiveMatch_Found()
    {
        var issues = new[] { Issue("Crash on startup") };
        var matches = GitHubIssues.FindDuplicates("crash ON startup", issues);
        Assert.Single(matches);
    }

    [Fact]
    public void FindDuplicates_SubstringEitherDirection_Found()
    {
        var issues = new[] { Issue("uictl crashes on Notepad") };
        Assert.Single(GitHubIssues.FindDuplicates("crashes on notepad", issues));

        var issues2 = new[] { Issue("crash") };
        Assert.Single(GitHubIssues.FindDuplicates("uictl crash on launch", issues2));
    }

    [Fact]
    public void FindDuplicates_NoOverlap_NotFound()
    {
        var issues = new[] { Issue("Unrelated feature request") };
        Assert.Empty(GitHubIssues.FindDuplicates("crash on startup", issues));
    }

    [Fact]
    public void FindDuplicates_EmptyTitle_ReturnsEmpty()
    {
        var issues = new[] { Issue("Anything") };
        Assert.Empty(GitHubIssues.FindDuplicates("   ", issues));
    }
}
