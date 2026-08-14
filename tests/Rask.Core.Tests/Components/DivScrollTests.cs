using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined StubComponent has no generated factory

namespace Rask.Core.Tests.Components;

// Div.OnScroll / OnScrollAsync: the `scroll` event wired through data-rask-on-scroll, dispatched
// into a typed ScrollEvent. Sync Action<ScrollEvent> and async Func<ScrollEvent, Task> siblings,
// the same typed-pair convention as OnClick/OnClickAsync.
public partial class DivScrollTests : global::Rask.Core.RaskMarkup
{
    private static JsonElement Payload =>
        JsonDocument.Parse("{\"scrollTop\":120,\"clientHeight\":300,\"scrollHeight\":2000}").RootElement;

    [Fact]
    public void Scroll_OutsideLiveContext_NotEmitted() =>
        Assert.Equal("<div></div>", Div.OnScroll(_ => { }).ToHtml());

    [Fact]
    public void Scroll_SyncAndAsync_BothEmitTheAttribute()
    {
        var sync = new StubComponent(() => Div.OnScroll(_ => { }));
        Assert.Equal("<div data-rask-on-scroll=\"h0\"></div>", sync.RenderAsLiveRoot());

        var async = new StubComponent(() => Div.OnScrollAsync(_ => Task.CompletedTask));
        Assert.Equal("<div data-rask-on-scroll=\"h0\"></div>", async.RenderAsLiveRoot());
    }

    [Fact]
    public async Task Scroll_SyncHandler_ReceivesParsedScrollEvent()
    {
        ScrollEvent? seen = null;
        var view = new StubComponent(() => Div.OnScroll(e => seen = e));
        var id = Markup.Attr(view.RenderAsLiveRoot(), "data-rask-on-scroll")!;

        await view.TryInvokeHandlerAsync(id, Payload);

        Assert.NotNull(seen);
        Assert.Equal(120, seen!.ScrollTop);
        Assert.Equal(300, seen.ClientHeight);
        Assert.Equal(2000, seen.ScrollHeight);
    }

    [Fact]
    public async Task Scroll_AsyncHandler_IsAwaited()
    {
        ScrollEvent? seen = null;
        var view = new StubComponent(() => Div
            .OnScrollAsync(e =>
        {
            seen = e;
            return Task.CompletedTask;
        }));
        var id = Markup.Attr(view.RenderAsLiveRoot(), "data-rask-on-scroll")!;

        await view.TryInvokeHandlerAsync(id, Payload);

        Assert.NotNull(seen);
        Assert.Equal(120, seen!.ScrollTop);
    }
}
