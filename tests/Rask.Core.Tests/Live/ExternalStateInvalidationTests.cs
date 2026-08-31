using System.Text;
using Rask.Core.Live;
using Rask.TestSupport;

#pragma warning disable RASK014 // test-defined component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

// The framework invariant behind ShowcaseLayout's route.Changed subscription (issue #420).
//
// A component that derives its UI from external state it does NOT own — a constructor-injected service read
// directly, like ShowcaseLayout reading RouteState.Path — is invisible to the render cache. Only *ambient*
// reads (Context.Get / EditContext) set _readsAmbientState and opt a component out of the cache; a plain
// service read does not. So such a component stays fully cache-eligible, and a clean re-render REPLAYS its
// stale subtree without re-executing Render(). The only thing that refreshes it is a dirty mark — which is
// exactly what StateHasChanged produces during a diff. That is *why* the component must subscribe to the
// source's Changed event (`route.Changed += StateHasChanged`): without it, an external change that nothing
// else touches is served stale from the cache. This test pins both halves.
public partial class ExternalStateInvalidationTests : global::Rask.Core.RaskMarkup
{
    // Stand-in for RouteState: state the component reads but the cache doesn't track.
    private sealed class ExternalSource
    {
        public string Value = "a";
    }

    private static string Render(SessionRenderCache cache, Component tree, List<EditOp> ops)
    {
        var sb = new StringBuilder();
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            HtmlSerializer.Serialize(tree, sb);
        }

        cache.TryComputeDiff(ops, rotate: true);
        return sb.ToString();
    }

    [Fact]
    public void UntrackedStateChange_WithoutInvalidation_ReplaysStale_WithInvalidation_ReWalks()
    {
        var source = new ExternalSource();
        var built = 0;
        // Pure-element subtree deriving its text from the untracked external source (the shape of a nav
        // link's active class computed from RouteState.Path).
        var view = new StubComponent(() =>
        {
            built++;
            return Div.Class("menu")[Span.Class("active")[source.Value]];
        });
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        var first = Render(cache, view, ops);
        Assert.Contains(">a<", first);
        Assert.Equal(1, built);
        // Reading external, non-ambient state leaves the subtree cache-eligible — the crux of the hazard.
        Assert.True(view.IsCleanSubtreeCachedForTest,
            "reading a ctor-injected service is not ambient state, so the subtree stays cacheable");

        // Hazard: the source changed, but nothing marked the component dirty → the clean subtree REPLAYS
        // the stale frame. Render() is not re-run, so the new value never reaches the DOM. This is the stale
        // active link / un-expanded group that a missing route.Changed subscription would leave behind.
        source.Value = "b";
        var stale = Render(cache, view, ops);
        Assert.Contains(">a<", stale);
        Assert.DoesNotContain(">b<", stale);
        Assert.Equal(1, built);   // replayed from frames, Render NOT re-executed
        Assert.Empty(ops);        // the cache believed nothing changed

        // Fix: mark the component dirty — the cache-level effect of StateHasChanged, which the subscription
        // fires on every Changed. Now the subtree re-walks and the new value ships as a single text edit.
        source.Value = "c";
        view.MarkDirtyForFrame();
        var fresh = Render(cache, view, ops);
        Assert.Contains(">c<", fresh);
        Assert.Equal(2, built);   // re-walked
        var op = Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, op.Kind);
        Assert.Equal("c", op.Value);
    }
}
