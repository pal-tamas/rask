using System.Runtime.CompilerServices;
using Rask.Core.Forms;
using Rask.Core.HeadAssets;
using Rask.Core.ScopedAssets;
using ErrorBoundary = Rask.Core.Components.ErrorBoundary;
using RouteRenderState = Rask.Core.Routing.RouteRenderState;

namespace Rask.Core.Live;

public sealed class LiveRenderContext : IDisposable
{
    private static readonly AsyncLocal<LiveRenderContext?> _current = new();

    // Thread-local fast mirror of _current, valid only during the synchronous render walk.
    // AsyncLocal.Value reads walk the execution context and are markedly slower than a field
    // read; the per-element attribute path reads "the current context" several times per
    // element. Serialize() and WriteAttributes() run synchronously on one thread between this
    // context's construction and disposal, so a ThreadStatic mirror is exact there. The
    // AsyncLocal _current stays authoritative for async continuations (which may resume on a
    // different thread and must still observe Current / IsActive) — those keep reading _current.
    [ThreadStatic] private static LiveRenderContext? _syncCurrent;

    private readonly LiveRenderContext? _previousSync;
    private readonly Stack<ErrorBoundary> _boundaryStack = new();
    private readonly Dictionary<ObjectKey, EditContext> _currentEditContexts;
    private readonly IRenderHandle? _handle;
    private readonly Stack<Component> _parentStack = new();
    private readonly LiveRenderContext? _previous;
    private readonly Dictionary<ObjectKey, EditContext> _previousEditContexts;

    private readonly Component _root;

    private readonly Stack<string> _scopeStack = new();

    // Distinguishes "we're inside the synchronous render walk" from "we're in an async
    // continuation that captured this ctx via AsyncLocal but the walk already finished".
    // AsyncLocal-captured ctx refs survive past Dispose: a continuation that fires after
    // the using-block exits still observes `Current == this`. Callers that need the
    // narrower meaning (e.g. RaskJSRuntime.BeginInvokeJS deciding whether the current
    // frame will drain a queued invoke for them) check IsActive instead of Current.

    private LiveRenderContext(
        Component root,
        Dictionary<ObjectKey, EditContext> previousEditContexts,
        Dictionary<ObjectKey, EditContext> currentEditContexts,
        IServiceProvider? services,
        HeadAssetRegistry headAssets,
        HashSet<Type> mountedTypes)
    {
        _root = root;
        _previousEditContexts = previousEditContexts;
        _currentEditContexts = currentEditContexts;
        Services = services;
        HeadAssets = headAssets;
        MountedTypes = mountedTypes;
        _handle = root.RenderHandle;
        // One context IS one render pass, and a pass is exactly the scope over which each component
        // numbers its handler slots from 0. Stamping the generation here rather than in
        // RenderAsLiveRootCore covers every path that can register a handler — Begin is public, and a
        // caller driving a root through it repeatedly would otherwise never see its slot counters reset.
        root.BeginHandlerGeneration();
        _previous = _current.Value;
        _previousSync = _syncCurrent;
        _current.Value = this;
        _syncCurrent = this;
    }

    internal bool IsActive { get; private set; } = true;

    public static LiveRenderContext? Current => _current.Value;

    // Fast, synchronous-walk-only accessor for the hot attribute path. Equals Current on the
    // render thread; null outside an active render. Do NOT use from async continuations — read
    // Current there.
    //
    // The IsActive guard enforces the "null outside an active render" contract even when the
    // ThreadStatic was not restored on this thread: an async render sets _syncCurrent in its ctor,
    // runs the synchronous walk, then awaits — releasing this pool thread with _syncCurrent still
    // pointing at the context (Dispose runs on whatever thread the continuation resumes on). A
    // later synchronous render reusing this thread would otherwise observe that stale context.
    // Reading through IsActive makes a disposed context read as "no context", which is correct.
    internal static LiveRenderContext? CurrentSync => _syncCurrent is { IsActive: true } ? _syncCurrent : null;

    // Test-only: clears this thread's sync mirror. xUnit reuses pool threads across tests; an
    // async render can release a thread at an await with _syncCurrent still set, so a later
    // synchronous test on that thread would otherwise observe a leftover context. Tests call
    // this before each test (see ResetLiveSyncContextAttribute). Not used by product code.
    internal static void ResetSyncForTests() => _syncCurrent = null;

    internal RouteRenderState? Route { get; set; }

    // The render engine, forwarded from the owning session (the render handle) and surfaced to components via
    // Component.HostEngine. Constant for the session → safe to read from Render() without the render-cache
    // ambient-state opt-out.
    internal RenderEngine Engine => _handle?.Engine ?? RenderEngine.Server;

    public IServiceProvider? Services { get; }

    public string? CurrentScopeId => _scopeStack.Count > 0 ? _scopeStack.Peek() : null;

    /// <summary>
    ///     Per-render collector for <see cref="Component.HeadAssets" /> contributions. The
    ///     <c>Generated.RaskHeadAssets</c> placeholder is replaced with this
    ///     registry's content during <see cref="Component.RenderAsLiveRoot()" />.
    /// </summary>
    internal HeadAssetRegistry HeadAssets { get; }

    /// <summary>
    ///     Byte offset of the <see cref="HeadAssetRegistry.Sentinel" /> within the freshly
    ///     serialized page, recorded by <c>HtmlSerializer</c> the moment it emits the sentinel
    ///     (the <c>&lt;head&gt;</c> branch). <see cref="Component.RenderAsLiveRoot()" /> reads it
    ///     to splice the head-asset block in place without a second whole-body <c>IndexOf</c>
    ///     scan. <c>-1</c> when no framework-managed <c>&lt;head&gt;</c> was serialized (e.g. the
    ///     partial-tree test path), matching an absent sentinel.
    /// </summary>
    internal int HeadSentinelIndex { get; set; } = -1;

    /// <summary>
    ///     Hands a development-only error to the session, to paint <em>over</em> the app. Called during
    ///     the render walk by <see cref="RootErrorBoundary" /> when a handler or async lifecycle hook threw.
    /// </summary>
    /// <remarks>
    ///     It goes to the handle rather than being held here because this context is disposed when the
    ///     walk ends and the frame is built afterwards. The session then rides it inside the render
    ///     payload rather than sending its own control frame — the same reasoning as <c>resume</c>,
    ///     <c>history</c> and <c>auth</c>: the frame stream is a contract, and an extra frame is
    ///     observable in ways an extra field is not.
    /// </remarks>
    internal void ReportDevError(DevErrorInfo error) => _handle?.ReportDevError(error);

    /// <summary>
    ///     Every user-component type observed during this render walk. Populated
    ///     unconditionally by <see cref="PushScope" /> on each component entry — covers
    ///     components with scoped CSS, scoped JS, both, and neither. Read by
    ///     <c>HeadAssetRegistry.ApplyTo</c> to emit one
    ///     <c>&lt;link&gt;</c>/<c>&lt;script&gt;</c> per mounted component that has a
    ///     registered scoped asset; types with neither contribute nothing.
    ///     <para>
    ///         A regression-prone shape: until the mounted-set was unconditional, only
    ///         components with CSS pushed onto the scope stack, so JS-only components
    ///         silently dropped out of head emission. The set must record types whether or
    ///         not <see cref="ScopedAssetRegistry.TryGetScopeId" /> finds a scope id.
    ///     </para>
    /// </summary>
    public HashSet<Type> MountedTypes { get; }

    internal ErrorBoundary? CurrentBoundary => _boundaryStack.Count > 0 ? _boundaryStack.Peek() : null;

    private Component CurrentParent => _parentStack.Count > 0 ? _parentStack.Peek() : _root;

    public void Dispose()
    {
        IsActive = false;
        _current.Value = _previous;
        _syncCurrent = _previousSync;
    }

    internal ContextScope PushScope(Component instance)
    {
        var type = instance.GetType();
        // Record the type unconditionally — MountedTypes is a public per-render contract populated for
        // every user component (with or without assets), so it can't be short-circuited.
        MountedTypes.Add(type);

        // The by-type scope lookup, however, always misses when no component has registered scoped CSS
        // (the common case), so skip the ConcurrentDictionary probe behind a cheap IsEmpty check.
        if (!ScopedAssetRegistry.HasAnyScopedCss || !ScopedAssetRegistry.TryGetScopeId(type, out var scopeId))
        {
            return default;
        }

        _scopeStack.Push(scopeId);
        return new ContextScope(this, ContextScopeKind.Scope);
    }

    internal ContextScope EnterParentScope(Component parent)
    {
        _parentStack.Push(parent);
        return new ContextScope(this, ContextScopeKind.Parent);
    }

    internal ContextScope PushBoundary(ErrorBoundary boundary)
    {
        // Stamp the boundary's own ancestor on first push, so an error in the boundary's
        // *fallback* (or its own async lifecycle) can still propagate to an outer boundary.
        boundary.SetParentBoundary(CurrentBoundary);
        _boundaryStack.Push(boundary);
        return new ContextScope(this, ContextScopeKind.Boundary);
    }

    public static LiveRenderContext Begin(Component root) =>
        new(root, new Dictionary<ObjectKey, EditContext>(), new Dictionary<ObjectKey, EditContext>(), null,
            new HeadAssetRegistry(), new HashSet<Type>());

    public static LiveRenderContext Begin(Component root, IServiceProvider? services) =>
        new(root, new Dictionary<ObjectKey, EditContext>(), new Dictionary<ObjectKey, EditContext>(), services,
            new HeadAssetRegistry(), new HashSet<Type>());

    // RenderAsLiveRootCore swaps two dictionaries it owns and passes both in here, so neither
    // the current nor the previous dict is allocated per render after warmup. It likewise
    // supplies a reused HeadAssetRegistry + MountedTypes set (cleared per render) so head
    // emission stops allocating ~5 collections every frame on the otherwise zero-alloc diff path.
    internal static LiveRenderContext Begin(
        Component root,
        Dictionary<ObjectKey, EditContext> previousEditContexts,
        Dictionary<ObjectKey, EditContext> currentEditContexts,
        IServiceProvider? services,
        HeadAssetRegistry headAssets,
        HashSet<Type> mountedTypes) =>
        new(root, previousEditContexts, currentEditContexts, services, headAssets, mountedTypes);

    public string RegisterHandler(Delegate handler) =>
        // Owner = the component currently rendering (top of parent stack). The root stores
        // every handler in its dictionary (TryInvokeHandlerAsync runs on the root), but the
        // owner association is what lets the post-handler dirty-mark land on the right node.
        _root.RegisterHandler(handler, CurrentParent);

    // The handler map lives on the root; the clean-subtree frame cache has to re-establish a skipped
    // walk's registrations in it, and only this context knows which root that is. The slot ids come
    // from the cached component itself. See Component.CachedSubtree.Handlers.

    /// <summary>Snapshot the run <paramref name="component" /> just registered (null if it registered none).</summary>
    internal (Component Owner, Delegate Handler)[]? CaptureHandlerRun(Component component) =>
        component.CaptureHandlerRun(_root);

    /// <summary>Re-register a captured run under its component's own slot ids, as the skipped walk would.</summary>
    internal void ReplayHandlerRun(Component component, (Component Owner, Delegate Handler)[] run) =>
        component.ReplayHandlerRun(_root, run);

    public T GetOrCreate<T>(Func<IServiceProvider, T> factory) where T : Component
    {
        var parent = CurrentParent;
        return parent.GetOrCreateChild(factory, Services, _handle);
    }

    public Component GetOrCreate(Type type, Func<IServiceProvider, Component> factory)
    {
        var parent = CurrentParent;
        return parent.GetOrCreateChild(type, factory, Services, _handle);
    }

    // The builder surface's GetOrCreate. Identical identity rules — same positional GetOrCreateChild —
    // but it also arms the parent's deferred commit, because an entry cannot call NotifyParameters the
    // way a factory does: its props arrive afterwards, through the setter chain. The parent fires it
    // for every entry-built child the moment its Render() returns (Component.RenderForLive).
    internal T GetOrCreateEntry<T>(
        Func<IServiceProvider, T> factory,
        Action<Component, ulong> pendingReset,
        ulong pending,
        bool hasLifecycle)
        where T : Component
    {
        var parent = CurrentParent;
        var child = parent.GetOrCreateChild(factory, Services, _handle);
        parent.ArmEntryCommitInternal();

        // The commit reads a missing LiveState as "this child never reached GetOrCreate, so there is
        // nothing to notify" — which is true of the tree the serializer builds inline, and false of
        // everything that comes through here. It only LOOKED true because a live session gives every
        // GetOrCreate'd child a render handle, and setting one allocates the state; a handle-less render
        // (ToHtml, a server-rendered first paint) does not, and a chain that names no FOLDING prop never
        // calls Track either, so nothing else allocated it. The child then silently skipped its whole
        // lifecycle — `Authorize[content]` wires its IUserProvider in OnMount and rendered as if nobody
        // were signed in.
        //
        // So the state is claimed here, for the components that have something to run. Not for all of
        // them: a plain tag has no hook to fire and no mount to remember, and giving one a LiveState
        // apiece is the per-node cost the entry surface exists to avoid (the factory pays it — it calls
        // NotifyParameters unconditionally). `hasLifecycle` is the generator's answer, computed from
        // whether the component overrides any of Component's own On* hooks, and it defaults to TRUE so
        // that anything not generated by this version is correct rather than fast.
        if (hasLifecycle)
        {
            child.EnsureLiveStateInternal();
        }

        if (pending != 0UL)
        {
            // The folding props this entry may leave behind. Recorded against the PARENT so the drain
            // at the end of its Render() knows which slots are its own — see BuilderRuntime.
            BuilderRuntime.PushSlot(parent, child, pendingReset, pending);
        }

        return child;
    }

    public void NotifyParameters(Component component, bool propsChanged) =>
        component.RaiseLifecycleBeforeRender(propsChanged);

    // Called from Context.Get/Required and EditContext.MarkReader while a component is mid-Render (so
    // the component sits on top of the parent stack). Flags it as reading untracked ambient state,
    // which permanently opts it out of the render cache — a later change to a provided context value or
    // to the EditContext's validation state must re-execute its Render() for it to observe the change
    // (same rationale as Router/ErrorBoundary's BypassRenderCache).
    internal void MarkCurrentReadsAmbientState() => CurrentParent.MarkReadsAmbientStateInternal();

    /// <summary>
    ///     Flags the rendering component as reading ambient state and hands it back, so a package
    ///     outside Core can re-render it when that state later changes.
    /// </summary>
    /// <remarks>
    ///     One method rather than two because the two must not be used apart: a caller that observes a
    ///     component without opting it out of the render cache gets a component that is told to
    ///     re-render and then serves its cached tree anyway — which looks exactly like the data never
    ///     arriving. <c>Rask.Query</c> is the caller; see its <c>Query{T}.Data</c> getter.
    ///     <para>Null when nothing is rendering, which is the normal case off the render path.</para>
    /// </remarks>
    internal static Component? ObserveAmbientState()
    {
        var context = CurrentSync;
        if (context is null)
        {
            return null;
        }

        var component = context.CurrentParent;
        component.MarkReadsAmbientStateInternal();
        return component;
    }

    public EditContext GetOrCreateEditContext(object model, Func<EditContext>? factory = null)
    {
        var key = new ObjectKey(model);
        if (_currentEditContexts.TryGetValue(key, out var current))
        {
            return current;
        }

        if (_previousEditContexts.TryGetValue(key, out var existing))
        {
            AttachRenderRequest(existing);
            _currentEditContexts[key] = existing;
            return existing;
        }

        var ctx = factory?.Invoke() ?? new EditContext(model);
        AttachRenderRequest(ctx);
        _currentEditContexts[key] = ctx;
        return ctx;
    }

    // Used by Form when a caller passes an explicit Context: so sibling input bound factories
    // (which run before Form.EnterChildrenScope pushes the scope) resolve to the same instance
    // via GetOrCreateEditContext(model) instead of auto-creating a fresh one.
    public void RegisterEditContext(EditContext ctx)
    {
        if (ctx is null)
        {
            throw new ArgumentNullException(nameof(ctx));
        }

        AttachRenderRequest(ctx);
        _currentEditContexts[new ObjectKey(ctx.Model)] = ctx;
    }

    // Wire EditContext's RequestRender hook to the root component's handle so
    // background events inside the context (currently the sticky-dismissal
    // timer the ValidatingIndicator depends on) can ask the live render to
    // re-paint. Idempotent — once attached, re-registering the same context
    // across renders skips. Skipped entirely for contexts created outside a
    // live render (unit tests build their own RenderingHandle and wire it
    // manually if they want the sticky-driven render).
    private void AttachRenderRequest(EditContext ctx)
    {
        if (_handle is null || ctx.RequestRender is not null)
        {
            return;
        }

        var handle = _handle;
        ctx.RequestRender = () => _ = handle.RequestRenderAsync();
    }

    // Used by Form to make a sub-object reachable through the same EditContext as its root
    // model, so a nested binding like Input.Bind(() => model.Address.Street) — whose acc.Target is
    // model.Address — resolves to the form's EditContext rather than auto-creating a separate
    // sub-object context. Last-write-wins: when a Form receives both Model and Context, the
    // generated factory's setter order is Model-then-Context, so the Model setter runs first
    // (auto-creating a stray ctx and registering subs against it), and the Context setter
    // must be able to overwrite those subs to point at the user-supplied Context.
    internal void RegisterEditContextForKey(object subModel, EditContext ctx)
    {
        if (subModel is null)
        {
            throw new ArgumentNullException(nameof(subModel));
        }

        if (ctx is null)
        {
            throw new ArgumentNullException(nameof(ctx));
        }

        _currentEditContexts[new ObjectKey(subModel)] = ctx;
    }

    internal Dictionary<ObjectKey, EditContext> SnapshotEditContexts() => _currentEditContexts;

    // Extension-style entry points that handle a null receiver. Lets HtmlSerializer's
    // `using (liveCtx?.PushScope(...))` pattern survive the rewrite from class-based
    // pop disposables to a ref struct — `?.` can't return a ref struct (no Nullable<T>
    // for ref structs), but a non-extension static taking `LiveRenderContext?` can.
    internal static ContextScope PushScopeOrNone(LiveRenderContext? ctx, Component instance)
        => ctx is null ? default : ctx.PushScope(instance);

    internal static ContextScope EnterParentScopeOrNone(LiveRenderContext? ctx, Component parent)
        => ctx is null ? default : ctx.EnterParentScope(parent);

    internal static ContextScope PushBoundaryOrNone(LiveRenderContext? ctx, ErrorBoundary boundary)
        => ctx is null ? default : ctx.PushBoundary(boundary);

    internal readonly struct ObjectKey : IEquatable<ObjectKey>
    {
        public ObjectKey(object value) => Value = value;
        public object Value { get; }
        public bool Equals(ObjectKey other) => ReferenceEquals(Value, other.Value);
        public override bool Equals(object? obj) => obj is ObjectKey k && Equals(k);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(Value);
    }

    // Pops the matching stack on Dispose. Was three separate sealed-class IDisposables
    // (ScopePopper / ParentPopper / BoundaryPopper), each allocated on the heap per
    // PushScope / EnterParentScope / PushBoundary call. For a 100-component render
    // that's 100+ small allocations per frame just for scope bookkeeping. Unifying
    // into one ref struct with a kind discriminator drops that to zero — the struct
    // lives on the caller's stack frame.
    internal enum ContextScopeKind : byte
    {
        None = 0,
        Scope = 1,
        Parent = 2,
        Boundary = 3
    }

    internal readonly ref struct ContextScope
    {
        private readonly LiveRenderContext? _ctx;
        private readonly ContextScopeKind _kind;

        internal ContextScope(LiveRenderContext ctx, ContextScopeKind kind)
        {
            _ctx = ctx;
            _kind = kind;
        }

        public void Dispose()
        {
            if (_ctx is not { } ctx)
            {
                return;
            }

            switch (_kind)
            {
                case ContextScopeKind.Scope:
                    if (ctx._scopeStack.Count > 0)
                    {
                        ctx._scopeStack.Pop();
                    }

                    break;
                case ContextScopeKind.Parent:
                    if (ctx._parentStack.Count > 0)
                    {
                        ctx._parentStack.Pop();
                    }

                    break;
                case ContextScopeKind.Boundary:
                    if (ctx._boundaryStack.Count > 0)
                    {
                        ctx._boundaryStack.Pop();
                    }

                    break;
            }
        }
    }
}
