using System.Buffers;
using System.Runtime.CompilerServices;
using Rask.Core.Forms;
using Rask.Core.HeadAssets;
using Rask.Core.Live;

namespace Rask.Core;

public abstract partial class Component
{
    internal string RenderAsLiveRoot() => RenderAsLiveRootCore(null, false, sink: null)!;

    internal string RenderAsLiveRoot(IServiceProvider services) => RenderAsLiveRootCore(services, false, sink: null)!;

    internal string RenderAsLiveRoot(IServiceProvider services, bool publishOnly) =>
        RenderAsLiveRootCore(services, publishOnly, sink: null)!;

    /// <summary>
    ///     Live-update variant of <see cref="RenderAsLiveRoot(IServiceProvider, bool)" /> that renders the
    ///     page into <paramref name="sink" />'s reused char buffer instead of a fresh string, so a live
    ///     session's diff path allocates nothing for the page HTML. The session reads the rendered chars
    ///     back via <see cref="RenderedHtmlBuffers.Current" />.
    /// </summary>
    internal void RenderAsLiveRootInto(IServiceProvider services, bool publishOnly, RenderedHtmlBuffers sink) =>
        RenderAsLiveRootCore(services, publishOnly, sink);

    // Returns the rendered page as a string when sink is null; when a sink is supplied the page is
    // written into it instead and null is returned (the caller reads sink.Current).
    private string? RenderAsLiveRootCore(IServiceProvider? services, bool publishOnly, RenderedHtmlBuffers? sink)
    {
        var html = WalkAsLiveRoot(services, publishOnly, sink, out var walkAgain);
        if (!walkAgain)
        {
            return html;
        }

        // The page's title changed, and a layout that shows it had already rendered when the walk reached
        // the page (see LiveRenderContext.SettleRouteTitle). Walked once more and no further: the title is
        // settled now, and a page whose title differs every time it is read must not hold the frame back
        // for ever. Publish-only, so nothing that has just had its OnRendered gets it twice.
        FrameSinkScope.Current?.Reset();
        return WalkAsLiveRoot(services, publishOnly: true, sink, out _);
    }

    private string? WalkAsLiveRoot(
        IServiceProvider? services, bool publishOnly, RenderedHtmlBuffers? sink, out bool walkAgain)
    {
        using var ctx = BeginRootRender(services, out var previousEditContexts);

        // Pooled per-frame scratch buffers held on the root component. RenderAsLiveRootCore
        // runs single-threaded per session (the WS dispatcher serializes via the session
        // lock), so reusing these in place is safe and saves three allocations per render
        // after warmup.
        Live.AlivePrev ??= new HashSet<Component>(ReferenceEqualityComparer.Instance);
        Live.AliveNow ??= new HashSet<Component>(ReferenceEqualityComparer.Instance);
        Live.ParentMap ??= new Dictionary<Component, Component>(ReferenceEqualityComparer.Instance);
        Live.AlivePrev.Clear();
        Live.AliveNow.Clear();
        Live.ParentMap.Clear();

        // Snapshot the alive set AND parent map BEFORE we touch _children. Walking via
        // every component's _children gives us the same view the previous successful render
        // produced. The parent map is needed in the dispose pass to suppress double-dispose
        // of descendants in a torn-down subtree.
        CollectAliveWithParents(this, Live.AlivePrev, Live.ParentMap);

        // RenderAsLiveRoot is the explicit "render now" entry point — called for the initial
        // GET, WS reconnect recovery render, hot reload, and from tests. Force the root to
        // re-execute Render() this frame; descendants still skip on their own diff. Without
        // this, a second RenderAsLiveRoot call with no descendant marked dirty would skip the
        // root, never re-binding closure-captured state or reading external mutable state.
        Live.StateDirty = true;
        RaiseLifecycleBeforeRender(false);

        var html = SerializePage(sink);
        walkAgain = ctx.SettleRouteTitle();
        NotifyTreeRendered(publishOnly, Live.AliveNow, Live.AlivePrev);
        DisposeDeparted(Live.AlivePrev, Live.AliveNow, Live.ParentMap);

        // Swap: the dict we wrote into this frame becomes next frame's `previous`, and
        // the now-stale previous becomes the pool that next frame will Clear and reuse.
        var snapshot = ctx.SnapshotEditContexts();
        // Dispose EditContexts that were alive last frame but weren't re-resolved this frame —
        // i.e. the form they back was unmounted. Their sticky-dismissal timers would otherwise
        // fire once more after teardown (pinning the context + render handle for the sticky
        // tail). Compare by instance, not key: a Form shares one EditContext across its root
        // model plus every sub-model key, so a context is dead only when no surviving key still
        // points at it. Guarded on Count so the common form-free page pays nothing.
        DisposeUnmountedEditContexts(previousEditContexts, snapshot);
        Live.EditContextsPool = Live.PersistedEditContexts;
        Live.PersistedEditContexts = snapshot;
        return html;
    }

    private LiveRenderContext BeginRootRender(
        IServiceProvider? services, out Dictionary<LiveRenderContext.ObjectKey, EditContext> previousEditContexts)
    {
        // Reuse the handler map across renders, but clear it: a component that left the tree must not
        // keep a live registration, and every component still in it re-registers during the walk (or,
        // for a replayed subtree, via ReplayHandlerRun). Lazy-init only on the very first render of
        // this component as a root.
        //
        // The IDS are not reset. They belong to (component, slot) and stick for the component's
        // lifetime, so a component that renders unchanged re-registers under exactly the ids already
        // baked into the page — which is what lets the diff leave its data-rask-on-* attributes alone
        // and lets its cached subtree replay. What restarts each component's own slot numbering is the
        // generation, stamped by LiveRenderContext's constructor a few lines below.
        Live.Handlers ??= new Dictionary<string, (Component, Delegate)>(StringComparer.Ordinal);
        Live.Handlers.Clear();
        // Lazily init on first root render — non-root Component instances (the 99% case for
        // leaf Elements in a page) never touch this field and stay allocation-free.
        previousEditContexts =
            Live.PersistedEditContexts ??= new Dictionary<LiveRenderContext.ObjectKey, EditContext>();
        // Recycle the previously-snapshotted dict as the next frame's `current`. First
        // render: pool is null, allocate once. Steady state: Clear and reuse.
        Live.EditContextsPool ??= new Dictionary<LiveRenderContext.ObjectKey, EditContext>();
        Live.EditContextsPool.Clear();
        // Reuse the head-asset collector and mounted-type set across renders (cleared here),
        // so head emission doesn't allocate fresh lists/sets every frame.
        Live.HeadAssets ??= new HeadAssetRegistry();
        Live.HeadAssets.Clear();
        Live.MountedTypes ??= new HashSet<Type>();
        Live.MountedTypes.Clear();
        return LiveRenderContext.Begin(
            this, previousEditContexts, Live.EditContextsPool, services, Live.HeadAssets, Live.MountedTypes);
    }

    private string? SerializePage(RenderedHtmlBuffers? sink)
    {
        // A live update writes straight into the session's own buffer (#1141); a page that becomes a
        // string goes through a pooled builder and materializes once, in the final ToString.
        var builder = sink is null ? RaskStringBuilderPool.Shared.Get() : null;
        var html = builder is null ? sink!.BeginWrite() : HtmlWriter.Over(builder);
        try
        {
            HtmlSerializer.Serialize(this, html);

            // Splice component-declared <head> contributions into the RaskHeadAssets sentinel, whose
            // offset was recorded during serialization (HeadSentinelIndex), so no whole-page scan.
            if (LiveRenderContext.Current is { } liveCtx)
            {
                // The splice shifts every char after the sentinel, and the diff codec's frame offsets were
                // captured before it — so when a frame stream is being captured, move them by the same
                // delta, or an InsertSubtree fragment sliced through them reads the wrong chars.
                var sentinelIdx = liveCtx.HeadSentinelIndex;
                var preLen = html.Length;
                liveCtx.HeadAssets.ApplyInPlace(html, sentinelIdx, liveCtx.Services);
                if (sentinelIdx >= 0 && FrameSinkScope.Current is { } frameSink)
                {
                    frameSink.AdjustOffsetsFrom(
                        sentinelIdx + HeadAssetRegistry.Sentinel.Length,
                        html.Length - preLen);
                }
            }

            return builder?.ToString();
        }
        finally
        {
            if (builder is null)
            {
                sink!.EndWrite(html);
            }
            else
            {
                html.Release();
                RaskStringBuilderPool.Shared.Return(builder);
            }
        }
    }

    private void NotifyTreeRendered(bool publishOnly, HashSet<Component> aliveNow, HashSet<Component> alivePrev)
    {
        // Post-render alive set: union of _children across the whole tree, reachable from root.
        // Components that re-rendered have fresh _children; components that skipped kept theirs.
        CollectAlive(this, aliveNow);

        // Mounts and unmounts are the difference between these two sets; the devtools work that out themselves.
        RaskDevToolsHook.Active?.TreeCommitted(this, aliveNow, alivePrev);

#pragma warning disable S3267 // hot path: no enumerator/closure allocation
        foreach (var child in aliveNow)
#pragma warning restore S3267
        {
            if (!ReferenceEquals(child, this))
            {
                child.RaiseOnRendered(publishOnly);
            }
        }

        RaiseOnRendered(publishOnly);
    }

    private static void CollectAlive(Component root, HashSet<Component> seen)
    {
        Visit(root, seen);

        static void Visit(Component c, HashSet<Component> seen)
        {
            if (!seen.Add(c))
            {
                return;
            }

            if (c._live?.Children is null)
            {
                return;
            }

            foreach (var child in c._live.Children.Values)
            {
                Visit(child, seen);
            }
        }
    }

    // Dev-only (C# Hot Reload): mark every live, mounted component in the tree StateDirty so the next
    // render re-executes each Render() — including cached subtrees — against the freshly-applied IL. A
    // component with no LiveState has never rendered (no cache to bust), so it's skipped. Called from
    // LiveSessionBase.RerenderAllForHotReload under `dotnet watch`; best-effort (the caller swallows).
    internal static void MarkSubtreeDirtyInternal(Component root)
    {
        var seen = new HashSet<Component>();
        Visit(root, seen);

        static void Visit(Component c, HashSet<Component> seen)
        {
            if (!seen.Add(c))
            {
                return;
            }

            if (c._live is { IsUnmounted: false } live)
            {
                live.StateDirty = true;
            }

            if (c._live?.Children is null)
            {
                return;
            }

            foreach (var child in c._live.Children.Values)
            {
                Visit(child, seen);
            }
        }
    }

    private static void CollectAliveWithParents(
        Component root,
        HashSet<Component> seen,
        Dictionary<Component, Component> parents)
    {
        Visit(root, seen, parents);

        static void Visit(Component c, HashSet<Component> seen, Dictionary<Component, Component> parents)
        {
            if (!seen.Add(c))
            {
                return;
            }

            if (c._live?.Children is null)
            {
                return;
            }

            foreach (var child in c._live.Children.Values)
            {
                parents[child] = c;
                Visit(child, seen, parents);
            }
        }
    }
}
