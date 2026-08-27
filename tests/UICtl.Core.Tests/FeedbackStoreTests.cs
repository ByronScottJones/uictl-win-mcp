using UICtl.Core;

namespace UICtl.Core.Tests;

/// <summary>
/// Exercises FeedbackStore against the real %LOCALAPPDATA%\uictl\feedback.json
/// - same "hit the real thing, not a mock" philosophy as the rest of this
/// suite - but every entry a test creates is deleted again in Dispose so a
/// test run never leaves stray drafts behind for a human (or the CLI) to
/// stumble on afterward.
/// </summary>
public class FeedbackStoreTests : IDisposable
{
    private readonly List<int> _createdIds = [];

    private FeedbackEntry Create(string category = "recommendation", string title = "test entry", string body = "test body")
    {
        var entry = FeedbackStore.Create(category, title, body);
        _createdIds.Add(entry.Id);
        return entry;
    }

    public void Dispose()
    {
        foreach (int id in _createdIds)
        {
            try { FeedbackStore.Delete(id); }
            catch (UiCtlException) { /* already deleted by the test itself */ }
        }
    }

    [Fact]
    public void Create_ThenGet_RoundTrips()
    {
        var created = Create(title: "round trip title", body: "round trip body");
        var fetched = FeedbackStore.Get(created.Id);

        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("round trip title", fetched.Title);
        Assert.Equal("round trip body", fetched.Body);
        Assert.Equal("draft", fetched.Status);
        Assert.Null(fetched.SubmittedAt);
        Assert.Null(fetched.SubmittedUrl);
    }

    [Fact]
    public void Create_InvalidCategory_Throws()
    {
        Assert.Throws<UiCtlException>(() => FeedbackStore.Create("not-a-real-category", "t", "b"));
    }

    [Fact]
    public void List_IncludesCreatedEntry()
    {
        var created = Create(title: "should appear in list");
        Assert.Contains(FeedbackStore.List(), e => e.Id == created.Id);
    }

    [Fact]
    public void Update_ChangesOnlyGivenFields()
    {
        var created = Create(title: "original title", body: "original body");
        var updated = FeedbackStore.Update(created.Id, category: null, title: "new title", body: null);

        Assert.Equal("new title", updated.Title);
        Assert.Equal("original body", updated.Body);
        Assert.Equal(created.Category, updated.Category);
        Assert.True(updated.UpdatedAt >= created.UpdatedAt);
    }

    [Fact]
    public void Update_UnknownId_Throws()
    {
        Assert.Throws<UiCtlException>(() => FeedbackStore.Update(int.MaxValue, "issue", null, null));
    }

    [Fact]
    public void Delete_ThenGet_Throws()
    {
        var created = Create();
        FeedbackStore.Delete(created.Id);
        _createdIds.Remove(created.Id);

        Assert.Throws<UiCtlException>(() => FeedbackStore.Get(created.Id));
    }

    [Fact]
    public void MarkSubmitted_SetsStatusAndUrl()
    {
        var created = Create();
        var marked = FeedbackStore.MarkSubmitted(created.Id, "https://github.com/example/repo/issues/new?title=x");

        Assert.Equal("submitted", marked.Status);
        Assert.Equal("https://github.com/example/repo/issues/new?title=x", marked.SubmittedUrl);
        Assert.NotNull(marked.SubmittedAt);
    }

    [Fact]
    public void SubmissionUrl_EncodesTitleAndFoldsCategoryIntoBody()
    {
        var entry = new FeedbackEntry(1, "issue", "a b&c", "body text", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "draft", null, null);
        var url = FeedbackStore.SubmissionUrl(entry, "owner/repo").AbsoluteUri;

        Assert.StartsWith("https://github.com/owner/repo/issues/new?", url);
        Assert.Contains("title=a%20b%26c", url);
        Assert.Contains(Uri.EscapeDataString("**Category:** issue"), url);
    }
}
