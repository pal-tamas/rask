using System.Buffers;
using System.Runtime.CompilerServices;
using Rask.Core.Components;
using Rask.Core.Forms;
using Rask.Core.HeadAssets;
using Rask.Core.Live;

namespace Rask.Core;

public abstract partial class Component
{
    // Claims the LiveState for an entry-built child that has a lifecycle to run, so the commit below can
    // keep using "no LiveState" to mean "not mine to notify". See GetOrCreateEntry for why the two are
    // not the same question and why a handle-less render is where they came apart.
    internal void EnsureLiveStateInternal() => _ = Live;

    // The hoisted state — class so it stays out-of-band from each Component instance.
    // Field grouping mirrors the prior layout for readability of the diff.
    private sealed class LiveState
    {
        public HashSet<Component>? AliveNow;
        public HashSet<Component>? AlivePrev;
        // Hoisted off the base Component: only ever set on live-render roots and user components
        // (which allocate a LiveState anyway), so plain Elements shed these three refs entirely.
        public ErrorBoundary? Boundary;
        public IRenderHandle? RenderHandle;
        public CancellationTokenSource? LifetimeCts;
        public Component? CachedRenderResult;
        // Runs once Render() has returned, still inside the render context: per-render state kept
        // off the component (a query declared in Render) releases what this render no longer asked for.
        public Action? AfterRender;

        // The Head override's output, produced by the same render as CachedRenderResult and re-read
        // (never re-run) by every later collection of it — see Component.CachedHeadInternal. Null on
        // the components that have no Head, which is nearly all of them.
        public Component? CachedHead;

        // Phase B clean-subtree frame cache — see CachedSubtree. ONE reference, not the handful of
        // fields the snapshot actually needs: LiveState is allocated per node on a mounted page, so
        // every field here costs ~8 B on every node of every live session (measured: ~56 KB per field
        // on a 1,000-row page). Hanging the state off a side object moves that cost onto the
        // components that actually cache, and leaves the rest paying a single null reference.
        public CachedSubtree? Cached;
        public int ChildPositions;
        public Dictionary<(Type, int), Component>? Children;
        public Dictionary<LiveRenderContext.ObjectKey, EditContext>? EditContextsPool;
        public ElementRef? ElementRef;
        public string? Role;
        public int? TabIndex;
        public IReadOnlyDictionary<string, string?>? Aria;

        // Every remaining global HTML attribute, in one bag rather than a typed field each. A field here
        // costs ~8 B on every node of every live session (~56 KB per field on a 1,000-row page — see
        // CachedSubtree above), and the shared Element surface has a hard pending-bit budget for
        // folding properties, so one dictionary keeps both budgets where a typed prop each would keep
        // neither. This is the verbatim escape hatch behind Element.Attributes.
        public IReadOnlyDictionary<string, string?>? ExtraAttrs;

        // The typed global attributes that are neither plain-and-always-there (id/class/style/title)
        // nor cheap enough for a flag bit — see Component.GlobalAttrs. ONE reference for the six of
        // them, for exactly the reason the field above gives: six fields here would be ~336 KB on that
        // same 1,000-row page, where this is ~56 KB and only the elements that actually name one of
        // them allocate the side object. Hidden/Inert are in neither place — they are two bits each of
        // the flags byte, because `hidden` is common enough that allocating for it would be a
        // regression (see Element.FlagHiddenPresent).
        public GlobalAttrs? Globals;
        public HeadAssetRegistry? HeadAssets;
        public HashSet<Type>? MountedTypes;
        public Dictionary<string, (Component Owner, Delegate Action)>? Handlers;
        public bool HasInitialized;

        // Registered by the render walk rather than by a chain entry — an instance the app built itself. A
        // parent rebuilds its child map every render, so the walk has to register such an instance again
        // each time it meets it, or the next render reads it as removed and unmounts it while it is on screen.
        public bool AdoptedByWalk;
        public bool IsDisposed;
        public bool IsUnmounted;
        public HandlerState? HandlerState;
        public Dictionary<Component, Component>? ParentMap;
        public Dictionary<LiveRenderContext.ObjectKey, EditContext>? PersistedEditContexts;
        public Dictionary<(Type, int), Component>? PreviousChildren;
        public bool PropsDirty;
        public bool StateDirty;

        // Keyed child identity (#685). Three more null references on a parent that never keys a child,
        // and the maps are allocated only by one that does — a keyed list is a small fraction of the
        // nodes on a page, so this does not pay the per-node cost the note on Cached describes.
        //
        // KeyedTypes is deliberately CUMULATIVE rather than per-frame: once a parent has identified a
        // child type by key, that type stops being identified by its ordinal among ALL the parent's
        // children for the rest of the parent's life. It has to. A key that is new this frame must get a
        // FRESH instance, and the ordinal path would hand it a recycled one belonging to whichever item
        // used to sit at that position — which is the very bug being fixed, moved one step along.
        //
        // The value is what the parent keeps for that type beside the keyed children: the ones written
        // WITHOUT a key, which keep their instance by their order among themselves (#1215), the spare a
        // Key step set aside, and the keys written more than once. See KeyedSiblings.
        public Dictionary<Type, KeyedSiblings>? KeyedTypes;
        public Dictionary<(Type, object), Component>? KeyedChildren;
        public Dictionary<(Type, object), Component>? PreviousKeyedChildren;

        // The slot the last entry filed a child under. Read by that entry immediately (GetOrCreateEntry) and
        // carried on its EntrySlot, because by the time its Key step runs another entry may have moved it.
        public (Type Type, int Ordinal) LastChildSlot;

        // Builder surface: at least one child of THIS component came from a builder entry during the
        // Render() now in flight, so the post-Render commit loop has work to do. (Its partner, "a folding
        // setter changed a value", is a bit on the component itself — every chained tag sets it.)
        public bool HasEntryChildren;

        // An element in this component's subtree built entries during the WALK rather than during Render() —
        // a form's children function, `Form.Model(m)[f => [ … ]]`. Such entries are numbered on from this
        // component's child counter, which only a real render resets, so a render served from the cache would
        // build them all afresh: new instances, new handler ids, and an event already in flight against the
        // old ids dropped. Set by the walk, cleared by the next real render, read by the cache check.
        public bool BuildsChildrenInWalk;
    }
}
