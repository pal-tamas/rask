using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined StubComponent has no generated factory

namespace Rask.Core.Tests.Components;

// Div.OnScroll: the `scroll` event wired through data-rask-on-scroll. MDN's scroll is a plain Event, so the scroll
// box travels the way JavaScript reads it — as the target's state, e.Target.ScrollTop.
public partial class DivScrollTests : global::Rask.Core.RaskMarkup
{
    private static JsonElement Payload =>
        JsonDocument.Parse("{\"target\":{\"scrollTop\":120,\"clientHeight\":300,\"scrollHeight\":2000}}").RootElement;

    [Fact]
    public void Scroll_outside_live_context_not_emitted() =>
        Assert.Equal("<div></div>", Div.OnScroll(_ => { }).ToHtml());

    [Fact]
    public void Scroll_sync_and_async_both_emit_the_attribute()
    {
        var sync = new StubComponent(() => Div.OnScroll(_ => { }));
        Assert.Equal("<div data-rask-on-scroll=\"h0\"></div>", sync.RenderAsLiveRoot());

        var async = new StubComponent(() => Div.OnScroll(_ => Task.CompletedTask));
        Assert.Equal("<div data-rask-on-scroll=\"h0\"></div>", async.RenderAsLiveRoot());
    }

    [Fact]
    public async Task Scroll_sync_handler_reads_the_scroll_box_from_its_target()
    {
        Event? seen = null;
        var view = new StubComponent(() => Div.OnScroll(e => seen = e));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-scroll")!;

        await view.TryInvokeHandlerAsync(id, Payload);

        var box = seen?.Target;
        Assert.NotNull(box);
        Assert.Equal(120, box!.ScrollTop);
        Assert.Equal(300, box.ClientHeight);
        Assert.Equal(2000, box.ScrollHeight);
    }

    [Fact]
    public async Task Scroll_async_handler_is_awaited()
    {
        Event? seen = null;
        var view = new StubComponent(() => Div
            .OnScroll(e =>
        {
            seen = e;
            return Task.CompletedTask;
        }));
        var id = MarkupAssert.Attr(view.RenderAsLiveRoot(), "data-rask-on-scroll")!;

        await view.TryInvokeHandlerAsync(id, Payload);

        Assert.Equal(120, seen?.Target?.ScrollTop);
    }
}
