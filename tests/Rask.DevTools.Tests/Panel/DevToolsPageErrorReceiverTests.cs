using Rask.DevTools.Panel;
using Rask.DevTools.Probe;
using Rask.Testing;

namespace Rask.DevTools.Tests.Panel;

/// <summary>
///     The page's own failures reaching the Errors tab: a script's error listed as the page reported it, an island's named
///     with its phase and found inside the component that rendered it, and anything malformed dropped.
/// </summary>
public sealed class DevToolsPageErrorReceiverTests
{
    // App › Board (its <ul> at 0.1.0) › Chart island host at slot 2 of that list, inside ChartCard, which covers slots 2–3.
    private static DevToolsFeed Feed()
    {
        var feed = new DevToolsFeed();
        var chartCard = new DevToolsComponentNode(3, "ChartCard", null, [], [], At: "0.1.0.0|2|2");
        var list = new DevToolsComponentNode((2L << 20) | 1, "ul", null, [], [chartCard], IsTag: true, At: "0.1|0|1");
        var board = new DevToolsComponentNode(2, "Board", null, [], [list], At: "0.1|0|1");
        feed.RecordTree(new DevToolsComponentNode(1, "App", null, [], [board]));
        return feed;
    }

#pragma warning disable RASK014 // the receiver rendered alone, the way the panel page would chain it
    private static Page Receiver(DevToolsFeed feed) => Test.Render(new DevToolsPageErrorReceiver { Feed = feed });
#pragma warning restore RASK014

    private static Task Report(Page page, string json) =>
        page.On("[data-rask-devtools-page-errors]").RaiseAsync(
            "keydown", System.Text.Json.JsonSerializer.Serialize(new { key = DevToolsPageErrorReceiver.KeyPrefix + json }));

    [Fact]
    public async Task A_script_error_is_listed_as_the_page_reported_it()
    {
        var feed = Feed();
        var page = Receiver(feed);

        await Report(page,
            """{"kind":"page","title":"TypeError","message":"x is undefined","stack":"TypeError: x\n at app.js:3","at":1757926800000,"island":null,"phase":null,"place":null}""");

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
    public async Task A_failed_island_is_named_with_its_phase_and_found_inside_its_component()
    {
        var feed = Feed();
        var page = Receiver(feed);

        await Report(page,
            """{"kind":"island","title":"RangeError","message":"bad range","stack":null,"at":1,"island":"Chart","phase":"update","place":"0.1.0.0|2|1"}""");

        var error = Assert.Single(feed.Errors.Snapshot());
        Assert.Equal(DevToolsErrorKind.Island, error.Kind);
        Assert.Equal("'Chart' failed to update: bad range", error.Message);
        Assert.Equal(["Board", "ChartCard"], error.Path);
        Assert.Equal(3, error.ComponentId);
    }

    [Fact]
    public async Task Anything_that_is_not_a_report_is_dropped_and_long_fields_are_cut()
    {
        var feed = Feed();
        var page = Receiver(feed);

        await Report(page, "{not json");
        await Report(page, """{"kind":"page","message":"no title"}""");
        await page.On("[data-rask-devtools-page-errors]").RaiseAsync("keydown", """{"key":"Enter"}""");
        await Report(page, $$"""{"kind":"page","title":"Error","message":"{{new string('m', 5000)}}"}""");

        var error = Assert.Single(feed.Errors.Snapshot());
        Assert.Equal(2000, error.Message.Length);
    }

    [Fact]
    public void A_place_holds_its_own_slots_and_what_is_inside_them_and_the_innermost_component_wins()
    {
        var place = DevToolsPlaceMatch.Parse("0.1|2|2")!.Value;

        Assert.True(DevToolsPlaceMatch.Contains(place, [0, 1, 2]));
        Assert.True(DevToolsPlaceMatch.Contains(place, [0, 1, 3, 0, 4]));
        Assert.False(DevToolsPlaceMatch.Contains(place, [0, 1, 4]));
        Assert.False(DevToolsPlaceMatch.Contains(place, [0, 1]));
        Assert.False(DevToolsPlaceMatch.Contains(place, [0, 2, 2]));
        Assert.Null(DevToolsPlaceMatch.Parse("0.x|1|1"));
        Assert.Null(DevToolsPlaceMatch.Parse("0|1|0"));
        Assert.Equal([], DevToolsPlaceMatch.Parse("|0|1")!.Value.Path);

        var found = DevToolsPlaceMatch.Find(Feed().TreeSnapshot()!, [0, 1, 0, 0, 3, 1]);
        Assert.Equal("ChartCard", found!.Value.Component.Type);
        Assert.Null(DevToolsPlaceMatch.Find(Feed().TreeSnapshot()!, [0, 0, 5]));
    }
}
