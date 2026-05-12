using System.Runtime.CompilerServices;
using Rask.Core.Forms;
using Rask.Core.Routing;
using Rask.Core.ScopedCss;

namespace Rask.Core.Live;

public sealed class LiveRenderContext : IDisposable
{
    private static readonly AsyncLocal<LiveRenderContext?> _current = new();
    private readonly Dictionary<ObjectKey, EditContext> _currentEditContexts = new();
    private readonly IRenderHandle? _handle;
    private readonly Stack<Component> _parentStack = new();
    private readonly LiveRenderContext? _previous;
    private readonly Dictionary<ObjectKey, EditContext> _previousEditContexts;

    private readonly Component _root;
    private readonly Stack<string> _scopeStack = new();

    private LiveRenderContext(
        Component root,
        Dictionary<ObjectKey, EditContext> previousEditContexts,
        IServiceProvider? services)
    {
        _root = root;
        _previousEditContexts = previousEditContexts;
        Services = services;
        _handle = root.RenderHandle;
        _previous = _current.Value;
        _current.Value = this;
    }

    public static LiveRenderContext? Current => _current.Value;

    internal RouteRenderState? Route { get; set; }

    public IServiceProvider? Services { get; }

    public string? CurrentScopeId => _scopeStack.Count > 0 ? _scopeStack.Peek() : null;

    private Component CurrentParent => _parentStack.Count > 0 ? _parentStack.Peek() : _root;

    public void Dispose() => _current.Value = _previous;

    internal IDisposable? PushScope(Component instance)
    {
        if (!ScopedCssRegistry.TryRegister(instance, out var scopeId))
        {
            return null;
        }

        _scopeStack.Push(scopeId);
        return new ScopePopper(this);
    }

    internal IDisposable EnterParentScope(Component parent)
    {
        _parentStack.Push(parent);
        return new ParentPopper(this);
    }

    public static LiveRenderContext Begin(Component root) =>
        new(root, new Dictionary<ObjectKey, EditContext>(), null);

    public static LiveRenderContext Begin(Component root, IServiceProvider? services) =>
        new(root, new Dictionary<ObjectKey, EditContext>(), services);

    internal static LiveRenderContext Begin(
        Component root,
        Dictionary<ObjectKey, EditContext> previousEditContexts,
        IServiceProvider? services) =>
        new(root, previousEditContexts, services);

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

    internal readonly struct ObjectKey : IEquatable<ObjectKey>
    {
        public ObjectKey(object value) => Value = value;
        public object Value { get; }
        public bool Equals(ObjectKey other) => ReferenceEquals(Value, other.Value);
        public override bool Equals(object? obj) => obj is ObjectKey k && Equals(k);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(Value);
    }
}
