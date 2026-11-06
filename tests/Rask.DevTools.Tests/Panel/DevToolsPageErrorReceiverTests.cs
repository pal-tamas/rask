using Rask.DevTools.Panel;
using Rask.DevTools.Probe;
using Rask.Testing;

namespace Rask.DevTools.Tests.Panel;

/// <summary>
///     The page's own failures reaching the Errors tab: a script's error listed as the page reported it, and anything
///     malformed dropped.
/// </summary>
public sealed class DevToolsPageErrorReceiverTests
{
    private static DevToolsFeed Feed()
    {
        var feed = new DevToolsFeed();
        feed.RecordTree(new DevToolsComponentNode(1, "App", null, [], []));
        return feed;
    }

#pragma warning disable RASK014 // the receiver rendered alone, the way the panel page would chain it
    private static Page Receiver(DevToolsFeed feed) => Page.Render(new DevToolsPageErrorReceiver { Feed = feed });
#pragma warning restore RASK014

    private static Task Report(Page page, string json) =>
        page.On("[data-rask-devtools-page-errors]").Raise(
            "keydown", System.Text.Json.JsonSerializer.Serialize(new { key = DevToolsPageErrorReceiver.KeyPrefix + json }));

    [Fact]
    public async Task A_script_error_is_listed_as_the_page_reported_it()
    {
        var feed = Feed();
        var page = Receiver(feed);

        await Report(page,
            """{"kind":"page","title":"TypeError","message":"x is undefined","stack":"TypeError: x\n at app.js:3","at":1757926800000}""");

        var error = Assert.Single(feed.Errors.Snapshot());
        Assert.Equal(DevToolsErrorKind.Page, error.Kind);
        Assert.Equal("TypeError", error.Title);
        Assert.Equal("x is undefined", error.Message);
        Assert.StartsWith("TypeError: x", error.Detail);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1757926800000), error.At);
        Assert.Empty(error.Path);
        Assert.False(error.AppWide);
    }

    [Fact]
    public async Task Anything_that_is_not_a_report_is_dropped_and_long_fields_are_cut()
    {
        var feed = Feed();
        var page = Receiver(feed);

        await Report(page, "{not json");
        await Report(page, """{"kind":"page","message":"no title"}""");
        await page.On("[data-rask-devtools-page-errors]").Raise("keydown", """{"key":"Enter"}""");
        await Report(page, $$"""{"kind":"page","title":"Error","message":"{{new string('m', 5000)}}"}""");

        var error = Assert.Single(feed.Errors.Snapshot());
        Assert.Equal(2000, error.Message.Length);
    }
}
