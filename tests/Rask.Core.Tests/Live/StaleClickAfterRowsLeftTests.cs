using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.Core.Tests.Live;

// A list answers a search with fewer rows, and the pointer then comes over one of them: the hover is a render
// of its own, so the click that follows it at once was read from the page before that render. Its row is still
// there, and it is found by what tells the rows of one page apart — the row, not what the render worked out
// for all of them. The slots the shorter list no longer reaches still held the handlers of the long one, and
// counted among "the handlers that page had" they made every page's own working-out look like what tells rows
// apart: no row was the same, and the click ran nothing (a pick from a store's answer, on rask.sh).
public partial class StaleClickAfterRowsLeftTests : global::Rask.Core.RaskMarkup
{
    // What a render works out for all of its rows: new in every render, and never equal to the one before.
    private sealed class Walk(int number)
    {
        internal int Number { get; } = number;
    }

    [Fact]
    public async Task A_click_read_from_the_page_before_a_hover_picks_its_row_when_the_list_had_lost_rows_before_that()
    {
        string[] rows = ["Amsterdam", "Athens", "Berlin", "Bern", "Brussels"];
        var walks = 0;
        var picked = new List<string>();
        var page = new StubComponent(() =>
        {
            var walk = new Walk(++walks);
            return [Div[rows.Select(row => Button.Key(row).OnClick(() => picked.Add(row + " in walk " + walk.Number))[row])]];
        });
        using var cache = new SessionRenderCache();
        Render(cache, page);
        rows = ["Berlin", "Bern"];
        Render(cache, page);
        Render(cache, page);

        var ran = await page.TryInvokeHandlerAsync("h1", JsonDocument.Parse("""{"id":"h1","type":"click","v":1}""").RootElement);

        Assert.True(ran);
        Assert.Equal(["Bern in walk 3"], picked);
    }

    [Fact]
    public async Task A_click_read_from_the_long_list_on_a_row_the_short_one_dropped_runs_nothing()
    {
        string[] rows = ["Amsterdam", "Athens", "Berlin", "Bern", "Brussels"];
        var walks = 0;
        var picked = new List<string>();
        var page = new StubComponent(() =>
        {
            var walk = new Walk(++walks);
            return [Div[rows.Select(row => Button.Key(row).OnClick(() => picked.Add(row + " in walk " + walk.Number))[row])]];
        });
        using var cache = new SessionRenderCache();
        Render(cache, page);
        rows = ["Berlin", "Bern"];
        Render(cache, page);
        Render(cache, page);

        var dropped = await page.TryInvokeHandlerAsync("h4", JsonDocument.Parse("""{"id":"h4","type":"click","v":0}""").RootElement);
        var kept = await page.TryInvokeHandlerAsync("h3", JsonDocument.Parse("""{"id":"h3","type":"click","v":0}""").RootElement);

        Assert.False(dropped);
        Assert.True(kept);
        Assert.Equal(["Bern in walk 3"], picked);
    }

    // Renders, and says the browser was sent the result.
    private static void Render(SessionRenderCache cache, Component page)
    {
        string html;
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            html = page.RenderAsLiveRoot();
        }

        cache.TryComputeDiff([], out _, html);
        page.HandlersSent();
    }
}
