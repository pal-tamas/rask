using System.Runtime.CompilerServices;
using Rask.Core.Forms;
using Rask.Core.HeadAssets;
using Rask.Core.ScopedCss;
using Rask.Core.ScopedJs;
using ErrorBoundary = Rask.Core.Components.ErrorBoundary;
using RouteRenderState = Rask.Core.Routing.RouteRenderState;

namespace Rask.Core.Live;

public sealed class LiveRenderContext : IDisposable
{
    private static readonly AsyncLocal<LiveRenderContext?> _current = new();
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
    private bool _active = true;
    internal bool IsActive => _active;

    private LiveRenderContext(
        Component root,
        Dictionary<ObjectKey, EditContext> previousEditContexts,
        Dictionary<ObjectKey, EditContext> currentEditContexts,
        IServiceProvider? services)
    {
        _root = root;
        _previousEditContexts = previousEditContexts;
        _currentEditContexts = currentEditContexts;
        Services = services;
        _handle = root.RenderHandle;
        _previous = _current.Value;
        _current.Value = this;
    }

    public static LiveRenderContext? Current => _current.Value;

    internal RouteRenderState? Route { get; set; }

    public IServiceProvider? Services { get; }

    public string? CurrentScopeId => _scopeStack.Count > 0 ? _scopeStack.Peek() : null;

    /// <summary>
    ///     Per-render collector for <see cref="Component.Head" /> contributions. The
    ///     <see cref="Generated.RaskHeadAssets" /> placeholder is replaced with this
    ///     registry's content during <see cref="Component.RenderAsLiveRoot()" />.
    /// </summary>
    internal HeadAssetRegistry HeadAssets { get; } = new();

    /// <summary>
    ///     Every user-component type observed during this render walk. Populated
    ///     unconditionally by <see cref="PushScope" /> on each component entry — covers
    ///     components with scoped CSS, scoped JS, both, and neither. Read by
    ///     <see cref="HeadAssetRegistry.ApplyTo" /> to emit one
    ///     <c>&lt;link&gt;</c>/<c>&lt;script&gt;</c> per mounted component that has a
    ///     registered scoped asset; types with neither contribute nothing.
    ///     <para>
    ///         A regression-prone shape: until the mounted-set was unconditional, only
    ///         components with CSS pushed onto the scope stack, so JS-only components
    ///         silently dropped out of head emission. The set must record types whether or
    ///         not <see cref="Rask.Core.ScopedCss.ScopedCssRegistry.TryRegister" /> finds
    ///         a scope id.
    ///     </para>
    /// </summary>
    public HashSet<Type> MountedTypes { get; } = new();

    internal ErrorBoundary? CurrentBoundary => _boundaryStack.Count > 0 ? _boundaryStack.Peek() : null;

    private Component CurrentParent => _parentStack.Count > 0 ? _parentStack.Peek() : _root;

    public void Dispose()
    {
        _active = false;
        _current.Value = _previous;
    }

    internal ContextScope PushScope(Component instance)
    {
        var type = instance.GetType();
        // Record the type unconditionally — head emission iterates this set and decides
        // per-type whether to emit a <link>/<script> based on registry hit. Doing the add
        // before the scope-id lookup keeps JS-only and asset-free components in the set.
        MountedTypes.Add(type);

        var hasCss = ScopedCssRegistry.TryRegister(type, out var scopeId);
        if (!hasCss)
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
        new(root, new Dictionary<ObjectKey, EditContext>(), new Dictionary<ObjectKey, EditContext>(), null);

    public static LiveRenderContext Begin(Component root, IServiceProvider? services) =>
        new(root, new Dictionary<ObjectKey, EditContext>(), new Dictionary<ObjectKey, EditContext>(), services);

    // RenderAsLiveRootCore swaps two dictionaries it owns and passes both in here, so neither
    // the current nor the previous dict is allocated per render after warmup.
    internal static LiveRenderContext Begin(
        Component root,
        Dictionary<ObjectKey, EditContext> previousEditContexts,
        Dictionary<ObjectKey, EditContext> currentEditContexts,
        IServiceProvider? services) =>
        new(root, previousEditContexts, currentEditContexts, services);

    public string RegisterHandler(Delegate handler) =>
        // Owner = the component currently rendering (top of parent stack). The root stores
        // every handler in its dictionary (TryInvokeHandlerAsync runs on the root), but the
        // owner association is what lets the post-handler dirty-mark land on the right node.
        _root.RegisterHandler(handler, CurrentParent);

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

    public void NotifyParameters(Component component, bool propsChanged) =>
        component.RaiseLifecycleBeforeRender(propsChanged);

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
    // model, so a nested binding like Input(() => model.Address.Street) — whose acc.Target is
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
                    if (ctx._scopeStack.Count > 0) ctx._scopeStack.Pop();
                    break;
                case ContextScopeKind.Parent:
                    if (ctx._parentStack.Count > 0) ctx._parentStack.Pop();
                    break;
                case ContextScopeKind.Boundary:
                    if (ctx._boundaryStack.Count > 0) ctx._boundaryStack.Pop();
                    break;
            }
        }
    }
}
