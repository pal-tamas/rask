using System.Text;
using Rask.Core.Live;
using Rask.TestSupport;

#pragma warning disable RASK014 // test-defined component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

// Phase B: a persistent, pure-element, handler-free user component caches its rendered subtree as a
// frame span and RELEASES its Element object graph; a clean re-render replays from frames (identical
// HTML, zero diff). A dirty re-render, a nested user component, or a handler keeps the element path.
public class CleanSubtreeReplayTests
{
    private static string Render(SessionRenderCache cache, Component tree, List<EditOp> ops, bool rotate = true)
    {
        var sb = new StringBuilder();
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            HtmlSerializer.Serialize(tree, sb);
        }

        cache.TryComputeDiff(ops, rotate);
        return sb.ToString();
    }

    [Fact]
    public void PureElementComponent_IsCachedAndReleasesElementGraph()
    {
        var page = new StubComponent(() => Div(Class: "page")[Span(Class: "v")["static"], Div()["x"]]);
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        Render(cache, page, ops);

        Assert.True(page.IsCleanSubtreeCachedForTest, "pure-element subtree should be frame-cached");
        Assert.False(page.RetainsElementGraphForTest, "the Element graph should be released after caching");
    }

    [Fact]
    public void CleanReRender_ReplaysIdenticalHtmlWithZeroDiff()
    {
        var built = 0;
        var page = new StubComponent(() =>
        {
            built++;
            return Div(Class: "page")[Span(Class: "v")["hello"], Div()["static"]];
        });
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        var first = Render(cache, page, ops);
        var second = Render(cache, page, ops);

        Assert.Equal(first, second);          // replayed HTML is byte-identical
        Assert.Empty(ops);                    // clean subtree → no edit ops
        Assert.Equal(1, built);               // second render replayed from frames (Render NOT re-run)
    }

    [Fact]
    public void DirtyReRender_ReWalksAndUpdates()
    {
        var value = 1;
        var page = new StubComponent(() => Div(Class: "page")[Span(Class: "v")[value.ToString()]]);
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        var first = Render(cache, page, ops);
        Assert.Contains(">1<", first);

        // Mutate + mark dirty: the cached component must re-render (not stale-replay).
        value = 2;
        page.MarkDirtyForFrame();
        var second = Render(cache, page, ops);

        Assert.Contains(">2<", second);
        var op = Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, op.Kind);
        Assert.Equal("2", op.Value);
        // Still cacheable after the dirty walk → re-cached, graph released again.
        Assert.True(page.IsCleanSubtreeCachedForTest);
        Assert.False(page.RetainsElementGraphForTest);
    }

    [Fact]
    public void NestedUserComponent_IsNotCached_SoDescendantsCanUpdate()
    {
        var inner = new StubComponent(() => Span(Class: "inner")["a"]);
        var outer = new StubComponent(() => Div(Class: "outer")[inner]);
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        Render(cache, outer, ops);

        // The outer subtree contains a nested user component that could go dirty independently, so
        // outer keeps the element-walk path (replaying its frames would skip inner's re-render).
        Assert.False(outer.IsCleanSubtreeCachedForTest);
        Assert.True(outer.RetainsElementGraphForTest);
        // The pure-element inner IS cached.
        Assert.True(inner.IsCleanSubtreeCachedForTest);
    }

    [Fact]
    public void CacheableThenNonCacheable_InvalidatesStaleSnapshot()
    {
        // Regression: an async page renders a pure-element "loading" state first (cacheable, element
        // graph released), then re-renders into a component-bearing "loaded" state (not cacheable). The
        // stale "loading" snapshot must be dropped, or a later CLEAN root re-render would replay it and
        // revert the DOM back to the spinner. (This is the HttpFetchDemo E2E failure, reduced.)
        var loaded = false;
        var inner = new StubComponent(() => Span(Class: "inner")["loaded"]);
        var page = new StubComponent(() => loaded
            ? Div(Class: "box")[inner] // nested user component → not cacheable
            : Div(Class: "box")[Span(Class: "spin")["loading"]]); // pure elements → cacheable
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        var first = Render(cache, page, ops);
        Assert.Contains("loading", first);
        Assert.True(page.IsCleanSubtreeCachedForTest);

        // Transition to the component-bearing state.
        loaded = true;
        page.MarkDirtyForFrame();
        var second = Render(cache, page, ops);
        Assert.Contains("loaded", second);
        Assert.DoesNotContain("loading", second);
        Assert.False(page.IsCleanSubtreeCachedForTest, "the stale loading snapshot must be invalidated");

        // A later clean re-render must keep showing "loaded", NOT replay the stale "loading" snapshot.
        var third = Render(cache, page, ops);
        Assert.Contains("loaded", third);
        Assert.DoesNotContain("loading", third);
        Assert.Empty(ops); // nothing changed between render 2 and 3
    }

    [Fact]
    public void HandlerBearingComponent_IsNotCached()
    {
        // A LiveRenderContext is needed for event handlers to register + emit their data-rask-on-*
        // hooks. Handler ids are reissued positionally each root render, so a replayed span with a
        // baked-in id could collide with a sibling's — such a subtree must keep the element-walk path.
        var page = new StubComponent(() => Div(Class: "btn", OnClick: () => { })["click me"]);
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();
        var sb = new StringBuilder();

        using (LiveRenderContext.Begin(page))
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            HtmlSerializer.Serialize(page, sb);
        }

        Assert.Contains("data-rask-on-click", sb.ToString());
        Assert.False(page.IsCleanSubtreeCachedForTest, "handler-bearing subtree must not be frame-cached");
        Assert.True(page.RetainsElementGraphForTest);
    }

    [Fact]
    public void NestedDescendantStaysLiveAcrossReRenders()
    {
        var innerValue = 1;
        var inner = new StubComponent(() => Span(Class: "v")[innerValue.ToString()]);
        var outer = new StubComponent(() => Div(Class: "outer")[Div(Class: "hdr")["title"], inner]);
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        Render(cache, outer, ops);

        // Change only the nested inner and dirty it; outer stays clean. Because outer was NOT cached,
        // the walk descends into inner and picks up its new value — no staleness.
        innerValue = 2;
        inner.MarkDirtyForFrame();
        var second = Render(cache, outer, ops);

        Assert.Contains(">2<", second);
        var op = Assert.Single(ops);
        Assert.Equal("2", op.Value);
    }
}
