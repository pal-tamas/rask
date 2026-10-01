using System.Buffers;
using System.Runtime.CompilerServices;
using Rask.Core.Live;

namespace Rask.Core;

public abstract partial class Component
{
    // Phase B clean-subtree frame replay. A user component whose last render was cached as a frame
    // span (pure elements, no handlers, no nested user components — see TryCacheCleanSubtree) re-emits
    // its HTML and frames directly from that span instead of re-walking (and retaining) an Element
    // object graph. Returns true when it replayed; false when the component is dirty or was never
    // cached, in which case the caller walks it normally (re-rendering, and possibly re-caching).
    //
    // The clean test mirrors RenderForLive's short-circuit: no prop/state change, no cache bypass, and
    // no ambient-state read. A dirty component falls through so its fresh Render() runs; if it stays
    // eligible afterwards it re-caches, otherwise it reverts to the element path transparently.
    internal bool TryReplayCleanSubtree(HtmlWriter html, FrameWriter frames, LiveRenderContext? liveCtx)
    {
        var cached = _live?.Cached;
        if (cached is null
            || Live.PropsDirty || Live.StateDirty
            || BypassRenderCache || _readsAmbientState)
        {
            return false;
        }

        // Without a live context there is nowhere to put this subtree's registrations back, and the
        // root's map is cleared every render — replaying would leave its buttons silently dead. So a
        // handler-bearing snapshot needs one; fall through to a walk when there is none.
        if (cached.Handlers is not null && liveCtx is null)
        {
            return false;
        }

        // The captured frames carry a baked-in data-rask-key: this component's own Key forwarded onto
        // its first element, or a keyed ancestor's that our first element adopted. Neither dirties us
        // when it changes — Key is a reconciliation identity, excluded from the propsChanged fold (see
        // ComponentFactoryGenerator), and an ancestor's key was never our prop at all — so a clean
        // component can be sitting on a snapshot whose identity has since gone stale. Replaying it would
        // emit the wrong key and the diff would match this subtree against the wrong sibling, moving the
        // wrong DOM. Fall through to a walk instead: it re-emits under the current key and re-caches.
        //
        // The expression is the identity a walk would emit right now — our own Key when we have one (the
        // serializer arms it, overwriting whatever an ancestor forwarded), else the ancestor's key still
        // pending in the slot. Compared by object rather than by the stringified key: KeyString
        // allocates for a value key (int, Guid) and this runs per keyed node per render, which is
        // exactly the per-update allocation this cache exists to avoid. Object equality is conservative
        // — a Key that changes identity but stringifies the same merely costs a walk — which is the safe
        // direction to be wrong in.
        if (!Equals(Key ?? (object?)KeyForwardScope.Peek(), cached.KeyIdentity))
        {
            return false;
        }

        // Past every bail-out, so the map is only written once the replay is certain to happen — a
        // rejected replay must leave the root's handler state exactly as it found it, or the walk that
        // follows would run against registrations it did not make.
        //
        // The ids to re-register under are this component's own slot ids: settled for its lifetime, and
        // therefore still exactly the ids a walk would issue now no matter what the rest of the page did
        // this frame. That is the whole reason ids are per (component, slot) — there is no page-wide
        // counter left to arrive back at, so an upstream handler count moving no longer costs a re-walk.
        if (cached.Handlers is { } handlers)
        {
            liveCtx!.ReplayHandlerRun(this, handlers);
        }

        // Re-emit the HTML and re-write the full frame stream (with fresh offsets) into the active
        // writer in one pass — the replayed frames are identical to a fresh walk's, so the diff sees
        // no change, and no Element object graph is touched.
        HtmlSerializer.ReplayLeanFrames(cached.Frames.AsSpan(0, cached.FrameCount), html, frames);

        // Leave the forward slot exactly as a walk would have: empty. A walk either armed our own key
        // and cleared it in its finally, or let our first element consume an ancestor's. Replaying skips
        // both, so an ancestor's key would otherwise stay armed and leak onto the next sibling element.
        KeyForwardScope.Clear();
        return true;
    }

    // Phase B clean-subtree frame capture. Called right after a user component's subtree was walked
    // and serialized into <paramref name="frames" /> (starting at <paramref name="frameStart" />). When
    // the subtree is safe to replay from frames alone, snapshot its frame span and RELEASE the cached
    // Element subtree (CachedRenderResult) so the object graph is collectible — the retained cost drops
    // from the full element tree to a compact frame array. Safety requires:
    //   * no nested user component (a nested component could go dirty independently; replaying the
    //     parent's frames would skip its re-render and show stale content — <paramref name="hadNested" />),
    //   * no indexer Children, no Head contribution (would be dropped on replay), not collecting native
    //     chrome, and cache-eligible (no bypass / ambient-state read) — everything that would make a
    //     frame replay diverge from a walk.
    //
    // A Key does NOT disqualify, though it used to. The key baked into the span (this component's own,
    // or an ancestor's forwarded onto our first element) can change while we stay clean — Key is a
    // reconciliation identity, excluded from the propsChanged fold (see ComponentFactoryGenerator) — so
    // rather than refusing to cache keyed subtrees at all, the snapshot records the identity it was
    // captured under and TryReplayCleanSubtree refuses a replay when it no longer matches.
    //
    // The trade is deliberate and measured, and it is NOT a memory win: caching keyed rows costs ~4%
    // MORE retained memory (a per-row snapshot runs bigger than the small Element graph it releases,
    // ~+266 B/row at 1,000 rows — this cache pays off in bytes when one component snapshots many nodes,
    // not when many components each snapshot a few). It buys a cheaper UPDATE, which is what a user
    // actually feels: the element path re-walks the graph and re-stringifies every Key on every render
    // (a value key allocates — see Component.KeyString), while a replay does neither. On a 1,000-row
    // keyed list that is ~13% less allocation and ~15% less time PER UPDATE, i.e. less GC pressure on
    // every interaction, for a one-off 4% on the retained ceiling. Numbers from the `session-churn`
    // update-cost pass and `session-footprint` (tests/Rask.Benchmarks).
    //
    // <paramref name="forwardedKeyAtCapture" /> is the ambient forwarded key read BEFORE the walk (only
    // meaningful when this component has no Key of its own; ours would overwrite the slot). A KEYLESS
    // component's first element adopts an ancestor's forwarded key, baking someone else's identity into
    // our span — not covered by our own-Key check, and stale-replaying it was a live bug.
    //
    // Event handlers do not disqualify. RenderAsLiveRootCore clears the root's map every render, so a
    // replay that skipped the walk would leave its ids absent from the map — a silently dead button —
    // which is why the snapshot records the handler run and a replay re-registers it. What it does NOT
    // have to reproduce is a position in a page-wide counter: ids belong to (component, slot) and hold
    // for the component's lifetime, so the run goes back under the same ids it came from and nothing
    // upstream can invalidate it.
    internal void TryCacheCleanSubtree(
        FrameWriter frames, int frameStart, bool hadNested,
        string? forwardedKeyAtCapture, LiveRenderContext? liveCtx)
    {
        var count = frames.Count - frameStart;
        if (hadNested
            || Children is not null
            || BypassRenderCache
            || _readsAmbientState
            || CachedHeadInternal is not null
            || count <= 0)
        {
            // This component just walked (first render or a dirty re-render) into something we won't
            // cache — a nested component, a handler, nothing, etc. Any PRIOR snapshot (e.g. this
            // component cached a pure-element "loading" state, then re-rendered into a component-bearing
            // "loaded" state) is now stale, so drop it: otherwise a later clean re-render would replay the
            // outdated subtree and revert the DOM. The element path (CachedRenderResult, set by
            // RenderForLive this walk) stays intact.
            if (_live is not null)
            {
                _live.Cached = null;
            }

            return;
        }

        var span = frames.WrittenSpan.Slice(frameStart, count);

        // Reuse the existing snapshot array when it still fits, so a component that re-renders every
        // frame (e.g. a stateful counter page) re-captures with ZERO allocation — only a fresh or grown
        // subtree allocates. Without this the per-update allocation win regresses by the snapshot size.
        var cached = _live!.Cached ??= new CachedSubtree();
        var snapshot = cached.Frames;
        if (snapshot.Length < count)
        {
            snapshot = new LeanFrame[count];
        }

        CopyLeanFrames(span, snapshot);
        cached.Frames = snapshot;
        cached.FrameCount = count;
        // Record the identity this span was captured under so a later replay can prove it is still the
        // right one.
        // Same expression as the replay check, but against the forwarded key as it was BEFORE the walk:
        // by now our first element has consumed it, so the live slot no longer holds it.
        cached.KeyIdentity = Key ?? (object?)forwardedKeyAtCapture;
        // Snapshot the handler run this walk registered (empty run → null, so a handler-free subtree
        // pays nothing and its replay skips re-registration entirely).
        cached.Handlers = liveCtx?.CaptureHandlerRun(this);
        // Drop the Element object graph: a clean re-render now replays the frame span above.
        Live.CachedRenderResult = null;
        _live.Children = WithoutPlainTags(_live.Children);
        _live.PreviousChildren = WithoutPlainTags(_live.PreviousChildren);
    }

    // The position maps kept every tag the last Render() built, for that Render()'s successor to reuse — which
    // held a cached component's whole element graph alive beside the snapshot that replaces it. A plain tag has
    // no state to reuse, so a later dirty render simply builds fresh ones; anything with a handle of its own
    // stays. An emptied map goes too.
    private static Dictionary<(Type, int), Component>? WithoutPlainTags(Dictionary<(Type, int), Component>? map)
    {
        if (map is null)
        {
            return null;
        }

        foreach (var (slot, child) in map)
        {
            if (!child.OwnsRenderHandle)
            {
                map.Remove(slot);
            }
        }

        return map.Count == 0 ? null : map;
    }

    // Copy the lean fields; the held snapshot drops the per-render HTML offsets and diff-only
    // component ref (replay regenerates offsets), so it retains ~24 B/node instead of ~40.
    private static void CopyLeanFrames(ReadOnlySpan<RenderFrame> span, LeanFrame[] snapshot)
    {
        for (var i = 0; i < span.Length; i++)
        {
            ref readonly var f = ref span[i];
            snapshot[i] = new LeanFrame
            {
                Kind = f.Kind,
                Name = f.Name,
                Value = f.Value,
                SubtreeLength = f.SubtreeLength,
                SelfClosing = f.SelfClosing,
                Opaque = f.Opaque
            };
        }
    }

    // Test hooks for the Phase B clean-subtree frame cache: whether this component's rendered
    // subtree was cached as frames, and whether it still retains its Element object graph. A cached
    // component has the first true and the second false (the graph was released).
    internal bool IsCleanSubtreeCachedForTest => _live?.Cached is not null;

    // The render result OR the position maps: either keeps the element graph alive.
    internal bool RetainsElementGraphForTest =>
        _live is { } live
        && (live.CachedRenderResult is not null || live.Children is { Count: > 0 } || live.PreviousChildren is { Count: > 0 });

    // ---- Clean-subtree handler round-trip (see CachedSubtree.Handlers) -----------------------------
    //
    // These exist so a replayed subtree can reproduce the walk's effect on handler state: the root's
    // map is cleared every render, so a replay that re-emitted a handler's id without re-registering it
    // would leave a silently dead button. Called on the SLOT component (the one being cached), with the
    // root passed in because that is where the map lives.

    /// <summary>
    ///     The (owner, delegate) pairs this component registered during the walk that just finished, in
    ///     slot order, or <c>null</c> when it registered none. A cacheable subtree contains no nested
    ///     user component, so every handler inside it was registered under THIS component's slots —
    ///     which is what lets the run be described by a slot count rather than by a position in some
    ///     page-wide sequence that anything upstream could move.
    /// </summary>
    internal (Component Owner, Delegate Handler)[]? CaptureHandlerRun(Component root)
    {
        var state = _live?.HandlerState;
        var rootState = root._live?.HandlerState;

        // A stale stamp means this component registered nothing this render, so LocalCount still
        // belongs to an older generation and must not be read as a count.
        if (state is null
            || rootState is null
            || state.Stamp != rootState.Generation
            || state.LocalCount <= 0
            || root.Live.Handlers is not { } map)
        {
            return null;
        }

        var run = new (Component, Delegate)[state.LocalCount];
        for (var i = 0; i < run.Length; i++)
        {
            if (IssuedSlotId(state, i) is not { } id || !map.TryGetValue(id, out var entry))
            {
                // The run isn't what we assumed it was; caching it would risk a dead handler on replay.
                return null;
            }

            run[i] = entry;
        }

        return run;
    }

    /// <summary>
    ///     Re-register a captured run under this component's own slot ids, leaving the root's handler
    ///     map exactly as the skipped walk would have. The ids belong to the component rather than to a
    ///     page-wide sequence, so there is nothing upstream that could have invalidated them.
    /// </summary>
    internal void ReplayHandlerRun(Component root, (Component Owner, Delegate Handler)[] run)
    {
        var state = _live!.HandlerState!;
        var map = root.Live.Handlers ??= new Dictionary<string, (Component, Delegate)>(StringComparer.Ordinal);
        for (var i = 0; i < run.Length; i++)
        {
            // Non-null by construction: CaptureHandlerRun refuses a run whose slots weren't all issued,
            // and a slot id is never surrendered once minted.
            map[IssuedSlotId(state, i)!] = run[i];
        }
    }

    /// <summary>
    ///     Everything a clean-subtree snapshot needs, hung off <see cref="LiveState.Cached" /> so only
    ///     the components that actually cache pay for it — see the note on that field.
    ///     <para>
    ///         Held instead of the Element object graph: on capture the subtree's frames are leaned down
    ///         into <see cref="Frames" /> and <c>CachedRenderResult</c> is released, so a clean re-render
    ///         replays from here and never touches an element again. Everything else on this object exists
    ///         to prove the snapshot is still valid to replay — identity and handler wiring that a walk
    ///         would have re-established and a replay must reproduce exactly.
    ///     </para>
    /// </summary>
    private sealed class CachedSubtree
    {
        /// <summary>
        ///     The leaned-down frame span. <c>Frames.Length</c> may exceed <see cref="FrameCount" /> — the
        ///     array is reused across captures so a component that re-renders every frame re-captures with
        ///     zero allocation. <c>LeanFrame</c> (~24 B) rather than <c>RenderFrame</c> (~40 B): a held
        ///     snapshot never needs the per-render HTML offsets or the diff-only component ref, both of
        ///     which replay regenerates.
        /// </summary>
        public LeanFrame[] Frames = [];

        public int FrameCount;

        /// <summary>
        ///     The <c>data-rask-key</c> identity baked into <see cref="Frames" />: the component's own
        ///     Key, or — when it has none — the ancestor-forwarded key its first element adopted. One
        ///     slot, because only one can ever reach our elements (an own Key overwrites the forwarded
        ///     one). Either can change without dirtying the component, so the replay re-checks it.
        /// </summary>
        public object? KeyIdentity;

        /// <summary>
        ///     The subtree's event handlers in the order the walk registered them, or <c>null</c> when it
        ///     has none. Ids are NOT stored: a cached subtree has no nested user component, so entry
        ///     <c>i</c> is this component's own slot <c>i</c> and its id is read back off the slot table
        ///     — one less reference per entry, and the id is settled for the component's lifetime so
        ///     there is nothing to go stale. Retaining the delegates is not new retention: the released
        ///     Element graph held these very instances.
        /// </summary>
        public (Component Owner, Delegate Handler)[]? Handlers;
    }
}
