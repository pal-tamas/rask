using System.Buffers;
using System.Runtime.CompilerServices;
using Rask.Core.Components;
using Rask.Core.Live;

namespace Rask.Core;

public abstract partial class Component
{
    internal Component? RenderForLive()
    {
        if (CanServeCachedRender())
        {
            return Live.CachedRenderResult;
        }

        RotateChildMaps();
        Live.ChildPositions = 0;

        // Re-armed by the walk that follows when it still builds entries — see LiveState.BuildsChildrenInWalk.
        Live.BuildsChildrenInWalk = false;

        // Why this render ran is only worked out when a devtools probe is listening.
        var devTools = RaskDevToolsHook.Active;
        var devToolsStart = devTools is null
            ? 0
            : devTools.ComponentRendering(this, CurrentRenderCause());

        // The dirty flags are cleared BEFORE Render() reads the state they stand for, never after. A
        // StateHasChanged from another thread — an async lifecycle hook resuming on the thread pool while this
        // render runs — sets StateDirty only after it has changed that state, so clearing first keeps a request
        // that lands mid-render alive to force the next render. Clearing at the end wiped it, cached the output
        // that had missed it, and every later render replayed that output (#1067). Blazor orders it the same
        // way, clearing its pending-render flag before BuildRenderTree.
        //
        // A render that does not complete has not rendered, so whatever asked for it is put back and the next
        // walk retries instead of serving the cache: OR-ed in, so a request that arrived meanwhile is kept too.
        var wasPropsDirty = Live.PropsDirty;
        var wasStateDirty = Live.StateDirty;
        Live.PropsDirty = false;
        Live.StateDirty = false;
        var completed = false;

        try
        {
            RenderAndCommit(devTools, devToolsStart);
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                Live.PropsDirty |= wasPropsDirty;
                Live.StateDirty |= wasStateDirty;
            }
        }

        return Live.CachedRenderResult;
    }

    // Skip when nothing meaningful changed: no first-time render, no prop change, no
    // explicit StateHasChanged, and the component hasn't opted out of caching. The
    // serializer still walks Live.CachedRenderResult, so any descendant whose own
    // Live.StateDirty or Live.PropsDirty IS set will re-render itself — ancestors don't need to
    // re-execute to permit that.
    //
    // A component that renders nothing (Render() returns null) leaves CachedRenderResult null,
    // so it never hits this cache and re-runs its (trivial) Render() on each non-dirty walk.
    // That's fine: nothing-render is state-driven — such components set StateDirty when they
    // gain content — and null can't double as the "already rendered" sentinel.
    //
    // A non-Element component that has children cannot reuse its cache: its children arrive via
    // the `[...]` indexer (not a factory param, so absent from the prop-change check) and are
    // BAKED INTO its Render() output, so a changed child set — e.g. a conditional alert appearing
    // — would be silently dropped. Elements are exempt: their children are walked at serialization
    // time (RenderChildren), never embedded in the cached result, so the cache stays valid. This is
    // what lets composite wrappers (a Bs* card around dynamic content) behave like the inline
    // elements they replace without opting out of caching by hand.
    //
    // A component whose walk builds entries (a form's children function in its subtree) cannot either: those
    // entries keep their identity only through the positional reuse a real render sets up, so serving the
    // cache would mint fresh ones every frame — see LiveState.BuildsChildrenInWalk.
    private bool CanServeCachedRender() =>
        Live.CachedRenderResult is not null && !Live.PropsDirty && !Live.StateDirty
        && !BypassRenderCache && !_readsAmbientState
        && !BakesChildrenIntoRender && !Live.BuildsChildrenInWalk;

    // The devtools want to know WHY this render ran. RenderForLive asks while the flags that say so are still
    // intact — before it clears them for Render() — and only when a probe is listening.
    //
    // The flags come first and the empty cache last. A component reaching this failed the cache check,
    // so if no flag explains it the cache was simply empty — which is also true of a re-render whose clean subtree
    // was captured as frames, and of a component that renders null. Testing the empty cache first reported those
    // as fresh renders no matter what had actually dirtied them.
    // Whether this component's indexer children end up inside its own Render() output — true for a composite
    // that has any. An Element walks its children at serialization time instead, and says so by overriding.
    private protected virtual bool BakesChildrenIntoRender => Children is not null;

    private RenderCause CurrentRenderCause() => this switch
    {
        _ when Live.PropsDirty => RenderCause.Props,
        _ when Live.StateDirty => RenderCause.State,
        _ when BypassRenderCache => RenderCause.Forced,
        _ when _readsAmbientState => RenderCause.AmbientState,
        _ when BakesChildrenIntoRender => RenderCause.Children,
        _ => RenderCause.Uncached,
    };

    private void RotateChildMaps()
    {
        // Swap the two dictionaries instead of allocating a fresh map per render —
        // both fields persist across the component lifetime, so after first render
        // every subsequent render reuses the same two buffers. _children is cleared
        // before any new writes; Live.PreviousChildren retains the prior frame's entries
        // for GetOrCreateChild's reuse lookup. If this component has never had child
        // GetOrCreate calls (most Elements), both dicts stay null and the swap is a
        // no-op — GetOrCreateChild lazily allocates on first write.
        if (_live?.Children is not null)
        {
            // First-time swap: allocate the back buffer so the persistent two-dict pool
            // works steady-state. Subsequent renders just swap and Clear — no allocation.
            _live.PreviousChildren ??= new Dictionary<(Type, int), Component>();
            (_live.PreviousChildren, _live.Children) = (_live.Children, _live.PreviousChildren);
            _live.Children.Clear();
        }

        // The keyed map rotates on exactly the same discipline, and only for a parent that keys a child
        // at all — otherwise both stay null and this is two null checks (#685).
        if (_live?.KeyedChildren is not null)
        {
            _live.PreviousKeyedChildren ??= new Dictionary<(Type, object), Component>();
            (_live.PreviousKeyedChildren, _live.KeyedChildren) = (_live.KeyedChildren, _live.PreviousKeyedChildren);
            _live.KeyedChildren.Clear();
        }

        if (_live?.KeyedTypes is { } keyedTypes)
        {
#pragma warning disable S3267 // hot path: no enumerator/closure allocation
            foreach (var siblings in keyedTypes.Values)
#pragma warning restore S3267
            {
                siblings.Rotate();
            }
        }
    }

    // HtmlSerializer wraps every user-component serialization in an EnterParentScope so
    // the scope is live during BOTH Render() and the walk of its returned subtree —
    // factories inside Render and handlers registered on elements deep in the tree both
    // attribute back to this component.
    //
    // A Render() that THROWS is a supported path — an ancestor ErrorBoundary catches it and
    // re-renders a fallback — so the entries it built before the throw still have to be swept off
    // the per-thread slot stack, which is only ever popped by the render that pushed onto it. A
    // stranded slot pins a live subtree on a pooled thread AND corrupts the next successful render
    // of this same component: that render pushes a second slot for the same target (positional
    // identity hands back the same instance), the stale one drains first, and its stale pending
    // mask blanks a prop the new chain has just set. Only the reset half runs on the way out —
    // firing lifecycle while an exception unwinds could throw again and swallow the original fault,
    // and it would be notifying a render that never happened.
    private void RenderDrainingOnThrow()
    {
        try
        {
            Live.CachedRenderResult = Render();
        }
        catch
        {
            if (Live.HasEntryChildren)
            {
                Live.HasEntryChildren = false;
                BuilderRuntime.DrainSlots(this);
            }

            throw;
        }
    }

    private void RenderAndCommit(IRaskDevToolsProbe? devTools, long devToolsStart)
    {
        RenderDrainingOnThrow();

        devTools?.ComponentRendered(this, devToolsStart);

        // Still inside the render context, so whatever keeps per-render state for this component — a
        // query declared in Render — can drop what this render no longer asked for.
        _live?.AfterRender?.Invoke();

        // The Head override is part of THIS component's render, not of the walk that serializes it.
        // Evaluating it here rather than at the serializer's collection point — which runs in the
        // ENCLOSING component's parent scope, after that component's own render has finished and
        // drained — is what gives an entry inside a Head override the right owner: its identity comes
        // from this component's positional child map (counted on from the render's own children, since
        // the reset above already ran), its pending reset drains with the rest below rather than a
        // frame late, and a Context read inside a Head marks THIS component as ambient-reading.
        //
        // Cached alongside the render result because the registry that collects it is rebuilt every
        // frame while this component may be served from the render cache: re-running the chain on a
        // cache hit would hand out fresh positional identities on every frame (the counter is only
        // reset by a real render), and re-running it at all is work a clean component does not owe.
        Live.CachedHead = HeadAssets;

        // Builder-surface commit point. A generated FACTORY assigns every prop and then calls
        // NotifyParameters itself, because it knows when the props are done. A setter chain has no
        // natural end — `Div.Class("a").Id("b")` could take another setter or the `[...]` indexer — so
        // the entries defer that half to here: the moment Render() returns, every chain it built is
        // complete and nothing can touch those props again before the walk reaches them.
        //
        // This is the exact factory ordering, not an approximation: the factory notifies during the
        // parent's Render(), i.e. with the same ambient state (no Context provider pushed yet, since
        // providers are pushed by the serializer) and always before the child is walked. Which is what
        // makes Live.PropsDirty land in time for RenderForLive's cache check and TryReplayCleanSubtree
        // on the child, and why a child that was built but then dropped from the tree still mounts.
        //
        // Gated on a flag armed by the entries themselves (LiveRenderContext.GetOrCreateEntry), so a
        // tree built entirely from factories never walks the child map here.
        if (Live.HasEntryChildren)
        {
            CommitEntryChildren();
        }
    }

    // Read without allocating the live state: the walk asks it of every children function's enclosing component.
    internal int ChildPositionsInternal => _live?.ChildPositions ?? 0;

    internal void MarkBuildsChildrenInWalk() => Live.BuildsChildrenInWalk = true;

    /// <summary>
    ///     Completes any chain this component owns that was built AFTER its own render finished.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A children-function — <c>Form.Model(m)[submitting =&gt; [ … ]]</c> — runs during the
    ///         SERIALIZER's walk, not during the owner's <c>Render()</c>. Its entries therefore land
    ///         after <see cref="RenderForLive" />'s commit point has already passed, so nothing drains
    ///         their pending resets and nothing calls <c>NotifyParameters</c> on what they built.
    ///     </para>
    ///     <para>
    ///         Left that way, a component in there keeps <c>PropsDirty</c> unset and the render cache
    ///         hands back the subtree from before the argument changed: the label a call site correctly
    ///         wrote as <c>submitting ? "Saving…" : "Sign up"</c> never leaves "Sign up" (#1050). An
    ///         explicit <c>Key</c> hid it by resolving to a different instance, which no call site
    ///         should have had to know.
    ///     </para>
    ///     <para>
    ///         Gated on the same flag the render-time commit uses, so a walk that built no entries pays
    ///         one bool test.
    ///     </para>
    /// </remarks>
    internal void CommitEntryChildrenIfPending()
    {
        if (_live is { HasEntryChildren: true })
        {
            CommitEntryChildren();
        }
    }

    // Fires the deferred NotifyParameters for every child a builder entry produced during the Render()
    // that just finished. Kept out of RenderForLive so the hot path is a single bool test.
    private void CommitEntryChildren()
    {
        // A commit can build more entries. The hooks it runs are user code, they run while this
        // component is still the one in scope, and a factory or an entry called from one lands here —
        // after the sweep that was supposed to complete them. So the whole pass repeats until it finds
        // nothing new; the flag is armed by the entry itself, so the overwhelmingly common "the hooks
        // built nothing" case costs one more bool test. It terminates on anything the factory survives:
        // a hook that unconditionally builds a component whose hook does the same recurses on the
        // factory too, straight into a stack overflow, because there the notification is synchronous.
        do
        {
            Live.HasEntryChildren = false;

            // Ordering is load-bearing: the pending resets put every prop the chain did NOT name back
            // to the value the factory would have passed, folding each one into EntryPropsChanged as it
            // goes, so they have to run before the commit below reads that flag.
            BuilderRuntime.DrainSlots(this);

            if (_live?.Children is { Count: > 0 } children)
            {
                CommitEach(children);
            }
        }
        while (Live.HasEntryChildren);
    }

    // Fires the commit over a SNAPSHOT of the child map rather than over the map itself. The lifecycle
    // hooks it runs may build components, and building one writes to this very dictionary
    // (GetOrCreateChild) — which, mid-enumeration, is an InvalidOperationException. It was not reachable
    // before the deferred commit existed: the factory notified from inside Render(), where the map is
    // written but never walked.
    //
    // The snapshot rides a per-thread buffer, reused across renders and unwound like a stack, so it
    // allocates nothing in the steady state and stays correct when a hook re-enters another component's
    // render (a nested ToHtml(), which commits onto the same buffer above our range).
    private static void CommitEach(Dictionary<(Type, int), Component> children)
    {
        var buffer = BuilderRuntime.CommitBuffer;
        var start = buffer.Count;
        try
        {
            foreach (var child in children.Values)
            {
                buffer.Add(child);
            }

            var end = buffer.Count;
            for (var i = start; i < end; i++)
            {
                buffer[i].CommitEntry();
            }
        }
        finally
        {
            buffer.RemoveRange(start, buffer.Count - start);
        }
    }

    // The child half of the commit. `!HasInitialized` is the mount signal and needs no flag of its own:
    // a factory-built child was already notified inside Render(), so it is initialized and this is a
    // no-op — RaiseLifecycleBeforeRender(false) on an initialized component does nothing at all.
    private void CommitEntry()
    {
        // No LiveState means the child never reached GetOrCreate (nothing to notify) — the same
        // no-context case in which the factory skips NotifyParameters too.
        var propsChanged = GetLifecycleFlag(FlagEntryPropsChanged);
        SetLifecycleFlag(FlagEntryPropsChanged, false);
        if (_live is not { } state || (state.HasInitialized && !propsChanged))
        {
            return;
        }

        RaiseLifecycleBeforeRender(propsChanged);
    }

    // Armed by LiveRenderContext.GetOrCreateEntry on the component whose Render() is building the tree.
    internal void ArmEntryCommitInternal() => Live.HasEntryChildren = true;

    // The positional slot the entry that just ran filed its child under, read by that entry straight away so
    // its own Key step can re-file exactly that slot later — see ClaimKeyedChild.
    internal (Type Type, int Ordinal) LastChildSlotInternal => Live.LastChildSlot;

    /// <summary>
    ///     Records that a builder setter wrote a value different from the one already on this component,
    ///     so the deferred commit reports <c>propsChanged: true</c>.
    /// </summary>
    /// <remarks>
    ///     The generated setters call this through <see cref="BuilderRuntime" /> (they are emitted into
    ///     the global namespace of every consuming assembly, so the entry point has to be public). It is
    ///     the setter-chain equivalent of the factory's <c>__propsChanged</c> fold: same
    ///     <see cref="EqualityComparer{T}" /> semantics, same exclusions — <c>Key</c>, auto-wrapped
    ///     callbacks and raw delegate props never fold, so the generator simply does not emit
    ///     the call for them.
    /// </remarks>
    internal void MarkEntryPropsChangedInternal() => SetLifecycleFlag(FlagEntryPropsChanged, true);

    // Marks this component dirty WITHOUT requesting a render, for the window of an async callback.
    //
    // AutoCallback calls it before awaiting a parent-supplied async delegate, which is what lets the
    // component show an intermediate state — a spinner around a fetch — with no StateHasChanged of its own.
    // The mid-await render (Component.InvokeWithRenderingAsync, driven by HandlerSyncContext when the user's
    // task actually yields) walks the tree and serves any clean component from the render cache; without this
    // the owner is still clean at that moment, so its `_loading = true` is invisible and the spinner only
    // "appears" after the fetch it was meant to cover.
    //
    // This mirrors what the DOM-handler path already does verbatim (TryInvokeHandlerAsync: "Set BEFORE
    // running so intermediate renders inside an async handler already see the owner as dirty"). The two paths
    // disagreeing is the bug: an async Button(OnClickAsync:) could paint mid-flight but the identical
    // BsDataGrid(OnSortChangeAsync:) could not.
    //
    // Deliberately not StateHasChanged(): that would also RequestRender, firing an extra render before
    // the user's code has run. Only the flag is wanted — the render is already coming.
    internal void MarkDirtyForAsyncHandler()
    {
        if (!Live.IsUnmounted)
        {
            Live.StateDirty = true;
        }
    }

    /// <summary>
    ///     Re-renders when an event fires — the shape a standard <see cref="EventHandler" /> subscribes, so a
    ///     component follows a source with <c>route.Changed += StateHasChanged;</c>.
    /// </summary>
    /// <param name="sender">The event's source; unused.</param>
    /// <param name="e">The event's argument; unused.</param>
    public void StateHasChanged(object? sender, EventArgs e) => StateHasChanged();

    public void StateHasChanged()
    {
        if (Live.IsUnmounted)
        {
            // Dropped: an unmount callback raising StateHasChanged during teardown is routine.
            return;
        }

        Live.StateDirty = true;
        RaskDevToolsHook.Active?.StateRequested(this);
        var handle = RenderHandle;
        if (handle is null)
        {
            return;
        }

        _ = handle.RequestRender();
    }

    // Internal-only equivalent of StateHasChanged that flips the dirty flag without
    // scheduling a render. RootErrorBoundary uses this to propagate "force the inner
    // root to re-execute Render() this frame" semantics — the same behavior
    // RenderAsLiveRootCore applies to its own root.
    internal void MarkDirtyForFrame() => Live.StateDirty = true;

    // Whether a render has been REQUESTED for this component but not yet performed — the half of
    // StateHasChanged that happens synchronously, and so the only observable moment the quiescence
    // ordering in InvokeAsyncLifecycleWithRendering can be pinned against (#1037).
    internal bool IsRenderRequestedForTest => _live is { StateDirty: true };

    // The awaitable StateHasChanged, for the host alone: a session re-rendering its whole tree waits for the frame.
    internal Task StateHasChangedAsync()
    {
        if (Live.IsUnmounted)
        {
            return Task.CompletedTask;
        }

        Live.StateDirty = true;
        RaskDevToolsHook.Active?.StateRequested(this);
        return RenderHandle?.RequestRender() ?? Task.CompletedTask;
    }
}
