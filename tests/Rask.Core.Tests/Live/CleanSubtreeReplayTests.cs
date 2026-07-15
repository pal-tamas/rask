using System.Text;
using System.Text.Json;
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

    // ---- Handler-bearing subtrees ----------------------------------------------------------
    //
    // Handler ids are positional and reissued from zero on every ROOT render, so these go through
    // RenderAsLiveRoot rather than HtmlSerializer.Serialize: only the real root path clears the map and
    // resets the counter, which is exactly the state a replay has to reproduce. The component under test
    // is a nested child (the root itself is never cacheable — it contains a user component).

    private static JsonElement EmptyPayload => JsonDocument.Parse("{}").RootElement;

    private static string RenderRoot(SessionRenderCache cache, Component root, List<EditOp> ops)
    {
        string html;
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            html = root.RenderAsLiveRoot();
        }

        cache.TryComputeDiff(ops);
        return html;
    }

    // Pulls the id out of the first data-rask-on-*="hN" in the emitted HTML — what the browser would
    // send back.
    private static string HandlerIdIn(string html, int occurrence = 0)
    {
        var idx = -1;
        for (var i = 0; i <= occurrence; i++)
        {
            idx = html.IndexOf("data-rask-on-", idx + 1, StringComparison.Ordinal);
            Assert.True(idx >= 0, $"expected at least {occurrence + 1} handler hook(s) in: {html}");
        }

        var open = html.IndexOf('"', idx) + 1;
        return html[open..html.IndexOf('"', open)];
    }

    [Fact]
    public void HandlerBearingComponent_IsCachedAndReleasesElementGraph()
    {
        // A button per row is what a real data grid looks like, so this shape has to cache like any
        // other pure-element subtree — the handler wiring is reproduced on replay rather than banned.
        var row = new StubComponent(() => Div(Class: "btn", OnClick: () => { })["click me"]);
        var root = new StubComponent(() => Div(Class: "page")[row]);
        var cache = new SessionRenderCache();

        var html = RenderRoot(cache, root, new List<EditOp>());

        Assert.Contains("data-rask-on-click", html);
        Assert.True(row.IsCleanSubtreeCachedForTest, "handler-bearing subtree should be frame-cached");
        Assert.False(row.RetainsElementGraphForTest, "the Element graph should be released after caching");
    }

    [Fact]
    public async Task ReplayedHandler_StillFires()
    {
        // The failure this guards is a silently dead button: a replay skips the walk, so unless it
        // re-registers the run, the id the browser sends back is absent from the freshly-cleared map.
        var clicks = 0;
        var row = new StubComponent(() => Div(Class: "btn", OnClick: () => clicks++)["click me"]);
        var root = new StubComponent(() => Div(Class: "page")[row]);
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        var first = RenderRoot(cache, root, ops);
        Assert.True(row.IsCleanSubtreeCachedForTest);

        var second = RenderRoot(cache, root, ops);
        Assert.Equal(first, second);   // replayed, byte-identical
        Assert.Empty(ops);

        var id = HandlerIdIn(second);
        Assert.True(await root.TryInvokeHandlerAsync(id, EmptyPayload), "the replayed id must still resolve");
        Assert.Equal(1, clicks);
    }

    [Fact]
    public async Task ReplayedHandlerOwnedByItsComponent_DirtyMarksThatComponent()
    {
        // The map stores (owner, delegate); the owner is what gets dirty-marked after a dispatch. A
        // replay must carry the resolved owner through, not just the delegate — otherwise the click
        // fires but the UI never updates.
        var counter = new CounterRow();
        var root = new StubComponent(() => Div(Class: "page")[counter]);
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        var first = RenderRoot(cache, root, ops);
        Assert.Contains(">0<", first);
        Assert.True(counter.IsCleanSubtreeCachedForTest);

        RenderRoot(cache, root, ops);   // replay
        await root.TryInvokeHandlerAsync(HandlerIdIn(first), EmptyPayload);

        // The dispatch dirtied the owner, so the next render must re-walk it and show the new value.
        var third = RenderRoot(cache, root, ops);
        Assert.Contains(">1<", third);
    }

    [Fact]
    public async Task ReplayedSubtree_AdvancesTheHandlerCounter()
    {
        // A replay that didn't advance the counter would hand the SECOND row the first row's id.
        var a = new StubComponent(() => Div(Class: "a", OnClick: () => { })["a"]);
        var b = new StubComponent(() => Div(Class: "b", OnClick: () => { })["b"]);
        var root = new StubComponent(() => Div(Class: "page")[a, b]);
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        var first = RenderRoot(cache, root, ops);
        var second = RenderRoot(cache, root, ops);

        Assert.Equal(first, second);
        Assert.NotEqual(HandlerIdIn(second, 0), HandlerIdIn(second, 1));
        // Both ids still resolve, so neither row's registration was lost or overwritten.
        Assert.True(await root.TryInvokeHandlerAsync(HandlerIdIn(second, 0), EmptyPayload));
        Assert.True(await root.TryInvokeHandlerAsync(HandlerIdIn(second, 1), EmptyPayload));
    }

    [Fact]
    public async Task HandlerIdsShiftUpstream_FallsBackToWalk()
    {
        // A handler appearing BEFORE a cached sibling shifts every later id by one. The sibling's baked
        // ids are then not what a walk would issue, so it must re-walk under the corrected ids rather
        // than replay a colliding span.
        var headerHandler = false;
        var rowClicks = 0;
        var row = new StubComponent(() => Div(Class: "row", OnClick: () => rowClicks++)["row"]);
        var root = new StubComponent(() => Div(Class: "page")[
            headerHandler ? Div(Class: "hdr", OnClick: () => { })["hdr"] : Div(Class: "hdr")["hdr"],
            row
        ]);
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        var first = RenderRoot(cache, root, ops);
        var rowIdBefore = HandlerIdIn(first);
        Assert.True(row.IsCleanSubtreeCachedForTest);

        // The header grows a handler and takes the row's old id.
        headerHandler = true;
        var second = RenderRoot(cache, root, ops);
        var headerId = HandlerIdIn(second, 0);
        var rowIdAfter = HandlerIdIn(second, 1);

        Assert.Equal(rowIdBefore, headerId);       // the header claimed the id the row used to hold
        Assert.NotEqual(rowIdAfter, headerId);     // the row moved rather than colliding
        Assert.True(await root.TryInvokeHandlerAsync(rowIdAfter, EmptyPayload));
        Assert.Equal(1, rowClicks);                // and it is still the ROW's handler behind it
    }

    // ---- Keyed subtrees ------------------------------------------------------------------
    //
    // A Key is forwarded onto the component's first rendered element and baked into its captured
    // frames as data-rask-key. Key is deliberately NOT part of the propsChanged diff (it is a
    // reconciliation identity, not a reactive prop — see ComponentFactoryGenerator), so a keyed
    // component can have its Key reassigned while staying clean. That is why the snapshot records the
    // Key it was captured under and refuses to replay against a different one; without that check a
    // key-only change would replay a stale data-rask-key and the diff would match the wrong sibling.

    [Fact]
    public void KeyedPureElementComponent_IsCachedAndReleasesElementGraph()
    {
        // A keyed list row is the shape where retained memory matters most (RASK022 wants a Key on
        // every list item), so it has to be cacheable like any other pure-element subtree.
        var page = new StubComponent(() => Div(Class: "row")[Span()["r1"]]) { Key = "k1" };
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        Render(cache, page, ops);

        Assert.True(page.IsCleanSubtreeCachedForTest, "a keyed pure-element subtree should be frame-cached");
        Assert.False(page.RetainsElementGraphForTest, "the Element graph should be released after caching");
    }

    [Fact]
    public void KeyedComponent_CleanReRender_ReplaysTheKeyIntoTheHtml()
    {
        var built = 0;
        var page = new StubComponent(() =>
        {
            built++;
            return Div(Class: "row")[Span()["r1"]];
        })
        { Key = "k1" };
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        var first = Render(cache, page, ops);
        var second = Render(cache, page, ops);

        Assert.Contains("data-rask-key=\"k1\"", first);
        Assert.Equal(first, second);   // the forwarded key survives the replay byte-for-byte
        Assert.Empty(ops);
        Assert.Equal(1, built);        // replayed from frames, Render NOT re-run
    }

    [Fact]
    public void KeyedComponent_KeyChangedWhileClean_DoesNotStaleReplay()
    {
        // The hazard the snapshot's key check exists for: reassigning Key does not dirty the component
        // (Key is excluded from the propsChanged fold), so a replay here would emit the OLD key and the
        // diff would reconcile this row against the wrong sibling.
        var page = new StubComponent(() => Div(Class: "row")[Span()["r1"]]) { Key = "k1" };
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        var first = Render(cache, page, ops);
        Assert.Contains("data-rask-key=\"k1\"", first);

        // Key changes, nothing else — the component is NOT marked dirty.
        page.Key = "k2";
        var second = Render(cache, page, ops);

        Assert.Contains("data-rask-key=\"k2\"", second);
        Assert.DoesNotContain("data-rask-key=\"k1\"", second);
        // Re-walked under the new key, so it re-caches and releases the graph again.
        Assert.True(page.IsCleanSubtreeCachedForTest);
        Assert.False(page.RetainsElementGraphForTest);
    }

    [Fact]
    public void KeyedComponent_KeyRemoved_DoesNotStaleReplay()
    {
        // Same hazard in the null direction: dropping the Key must drop the attribute, not replay it.
        var page = new StubComponent(() => Div(Class: "row")[Span()["r1"]]) { Key = "k1" };
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        Render(cache, page, ops);
        page.Key = null;
        var second = Render(cache, page, ops);

        Assert.DoesNotContain("data-rask-key", second);
    }

    [Fact]
    public void ForwardedKeyChangedWhileChildClean_DoesNotStaleReplay()
    {
        // Regression: a keyed OUTER whose body is a keyless nested component. Outer arms its key and
        // inner's first element consumes it, so OUTER's key is baked into INNER's cached frames — and
        // inner is clean and cacheable in its own right. When outer's key changes, nothing dirties
        // inner, so it would replay a snapshot carrying the old identity and the diff would match this
        // subtree against the wrong sibling. The snapshot records the forwarded key it was captured
        // under precisely so this falls back to a walk.
        var inner = new StubComponent(() => Div(Class: "row")[Span()["x"]]);
        var outer = new StubComponent(() => inner) { Key = "k1" };
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        var first = Render(cache, outer, ops);
        Assert.Contains("data-rask-key=\"k1\"", first);

        outer.Key = "k2";
        var second = Render(cache, outer, ops);
        Assert.Contains("data-rask-key=\"k2\"", second);
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

// A component that owns its handler (the closure captures `this`), so DelegateOwner resolves the owner
// to this instance and a dispatch dirty-marks it — the wiring ReplayedHandlerOwnedByItsComponent_* checks
// survives a frame replay.
internal sealed class CounterRow : Component
{
    private int _count;

    protected override Component? Render() =>
        Div(Class: "counter", OnClick: () => _count++)[_count.ToString()];
}
