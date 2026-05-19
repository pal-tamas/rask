using System.Runtime.CompilerServices;
using Rask.Core.Components;
using Rask.Core.Forms;
using Rask.Core.Routing;
using Rask.Core.HeadAssets;
using Rask.Core.ScopedCss;
using Rask.Core.ScopedJs;

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
    ///     Scope id awaiting attribution to the next body element written by HtmlSerializer.
    ///     Set by <see cref="PushScope"/> when the component type has registered JS; consumed
    ///     (and cleared) by the first body-element write of that component's render. The
    ///     consumer writes <c>data-rask-mount="{scopeId}"</c> so the browser-side dispatcher
    ///     can route <c>mount</c>/<c>unmount</c> calls to the right module.
    /// </summary>
    internal string? PendingMountScopeId { get; set; }

    /// <summary>
    ///     Per-render collector for <see cref="Component.Head"/> contributions. The
    ///     <see cref="Components.RaskHeadAssets"/> placeholder is replaced with this
    ///     registry's content during <see cref="Component.RenderAsLiveRoot()"/>.
    /// </summary>
    internal HeadAssetRegistry HeadAssets { get; } = new();

    private readonly List<ScopedJsInvoke> _scopedJsInvokes = new();

    /// <summary>
    ///     Per-render queue of explicit scoped-JS invocations. Populated by
    ///     <see cref="Component.InvokeScopedJs"/> calls from inside
    ///     <c>OnRendered</c> / <c>OnRenderedAsync</c>; consumed by the host's payload
    ///     builder which writes them as the <c>scopedJsInvokes</c> JSON field so the
    ///     client dispatcher fires <c>rendered(el, firstRender)</c> against matching
    ///     <c>data-rask-mount</c> elements after morph completes.
    /// </summary>
    internal IReadOnlyList<ScopedJsInvoke> ScopedJsInvokes => _scopedJsInvokes;

    internal void QueueScopedJsInvoke(string scopeId, string method, object?[]? args, int? invokeId = null) =>
        _scopedJsInvokes.Add(new ScopedJsInvoke(scopeId, method, args, invokeId));

    internal ErrorBoundary? CurrentBoundary => _boundaryStack.Count > 0 ? _boundaryStack.Peek() : null;

    private Component CurrentParent => _parentStack.Count > 0 ? _parentStack.Peek() : _root;

    public void Dispose() => _current.Value = _previous;

    internal IDisposable? PushScope(Component instance)
    {
        var type = instance.GetType();
        var hasCss = ScopedCssRegistry.TryRegister(type, out var scopeId);
        var hasJs = ScopedJsRegistry.IsRegistered(type);
        if (!hasCss && !hasJs)
        {
            return null;
        }

        // CSS and JS share the same scope id (both derived from CssScoper.ScopeIdFor). When
        // only JS is registered the CSS lookup misses; compute the id from the type directly
        // so the data-rask-mount stamp still lands on the right element.
        if (!hasCss)
        {
            scopeId = CssScoper.ScopeIdFor(type);
        }

        if (hasJs)
        {
            PendingMountScopeId = scopeId;
        }

        if (hasCss)
        {
            _scopeStack.Push(scopeId);
            return new ScopePopper(this);
        }

        // JS-only component: no CSS attr to stamp on descendants, so don't push a scope. The
        // pending-mount stamp is consumed by the first body element written below.
        return null;
    }

    internal IDisposable EnterParentScope(Component parent)
    {
        _parentStack.Push(parent);
        return new ParentPopper(this);
    }

    internal IDisposable PushBoundary(ErrorBoundary boundary)
    {
        // Stamp the boundary's own ancestor on first push, so an error in the boundary's
        // *fallback* (or its own async lifecycle) can still propagate to an outer boundary.
        boundary.SetParentBoundary(CurrentBoundary);
        _boundaryStack.Push(boundary);
        return new BoundaryPopper(this);
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
            _currentEditContexts[key] = existing;
            return existing;
        }

        var ctx = factory?.Invoke() ?? new EditContext(model);
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

        _currentEditContexts[new ObjectKey(ctx.Model)] = ctx;
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

    private sealed class ScopePopper : IDisposable
    {
        private readonly LiveRenderContext _ctx;
        public ScopePopper(LiveRenderContext ctx) => _ctx = ctx;

        public void Dispose()
        {
            if (_ctx._scopeStack.Count > 0)
            {
                _ctx._scopeStack.Pop();
            }
        }
    }

    private sealed class ParentPopper : IDisposable
    {
        private readonly LiveRenderContext _ctx;
        public ParentPopper(LiveRenderContext ctx) => _ctx = ctx;

        public void Dispose()
        {
            if (_ctx._parentStack.Count > 0)
            {
                _ctx._parentStack.Pop();
            }
        }
    }

    private sealed class BoundaryPopper : IDisposable
    {
        private readonly LiveRenderContext _ctx;
        public BoundaryPopper(LiveRenderContext ctx) => _ctx = ctx;

        public void Dispose()
        {
            if (_ctx._boundaryStack.Count > 0)
            {
                _ctx._boundaryStack.Pop();
            }
        }
    }

    internal readonly struct ObjectKey : IEquatable<ObjectKey>
    {
        public ObjectKey(object value) => Value = value;
        public object Value { get; }
        public bool Equals(ObjectKey other) => ReferenceEquals(Value, other.Value);
        public override bool Equals(object? obj) => obj is ObjectKey k && Equals(k);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(Value);
    }
}
