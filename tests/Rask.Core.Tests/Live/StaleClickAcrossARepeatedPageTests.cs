using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.Core.Tests.Live;

// A filter's cross pressed while the page is two renders ahead of the reader — the panel closing, then the rows
// arriving — and a third walk that rendered what the browser already had, so nothing was sent. The browser is two
// pages behind, which a slot remembers; the walk nobody was sent had pushed the page the press was read from out
// of that memory, and the press ran nothing (FiltersJourneyTests on a loaded runner).
public partial class StaleClickAcrossARepeatedPageTests : global::Rask.Core.RaskMarkup
{
    // What a render reads again from where it is kept: a new object every time, equal to the one before.
    private sealed record Filter(string Key);

    [Fact]
    public async Task A_press_read_two_pages_back_reaches_its_handler_after_a_walk_that_sent_nothing()
    {
        List<string> removed = [];
        var page = new StubComponent(() => Div[new[] { "driver", "q" }.Select(key =>
        {
            var filter = new Filter(key);
            return Button.Key(key).OnClick(() => removed.Add(filter.Key))[key];
        })]);
        using var cache = new SessionRenderCache();
        var readFrom = Render(cache, page, sent: true);
        Render(cache, page, sent: true);
        Render(cache, page, sent: true);
        Render(cache, page, sent: false);

        var ran = await page.TryInvokeHandlerAsync("h0", Click(readFrom));

        Assert.True(ran);
        Assert.Equal(["driver"], removed);
    }

    [Fact]
    public async Task A_press_read_two_pages_back_on_a_button_that_has_since_left_still_runs_nothing()
    {
        var shown = true;
        var (cancelled, deleted) = (0, 0);
        var page = new StubComponent(() =>
        {
            // Delete closes over what each render worked out, so every render makes a different one.
            var walk = new object();
            return Div[
                shown ? Button.OnClick(() => cancelled++)["Cancel"] : null,
                Button.OnClick(() => { GC.KeepAlive(walk); deleted++; })["Delete"]
            ];
        });
        using var cache = new SessionRenderCache();
        var readFrom = Render(cache, page, sent: true);
        shown = false;
        Render(cache, page, sent: true);
        Render(cache, page, sent: true);
        Render(cache, page, sent: false);

        var ran = await page.TryInvokeHandlerAsync("h0", Click(readFrom));

        Assert.False(ran);
        Assert.Equal((0, 0), (cancelled, deleted));
    }

    private static JsonElement Click(int version) =>
        JsonDocument.Parse($"{{\"type\":\"click\",\"v\":{version}}}").RootElement;

    // Renders, and says whether the browser was sent the result or already had it; answers the page's number.
    private static int Render(SessionRenderCache cache, Component page, bool sent)
    {
        string html;
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            html = page.RenderAsLiveRoot();
        }

        cache.TryComputeDiff([], out _, html);
        var version = page.HandlerVersionToSend(whole: true)!.Value;
        if (sent)
        {
            page.HandlersSent();
        }
        else
        {
            page.HandlersRepeated();
        }

        return version;
    }
}
