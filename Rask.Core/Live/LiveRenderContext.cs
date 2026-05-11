using System.Runtime.CompilerServices;
using Rask.Core.Forms;
using Rask.Core.Routing;
using Rask.Core.ScopedCss;

namespace Rask.Core.Live;

public sealed class LiveRenderContext : IDisposable
{
    private static readonly AsyncLocal<LiveRenderContext?> _current = new();
    private readonly Dictionary<(Type, int), Component> _currentChildren = new();
    private readonly Dictionary<ObjectKey, EditContext> _currentEditContexts = new();
    private readonly IRenderHandle? _handle;
    private readonly LiveRenderContext? _previous;
    private readonly Dictionary<(Type, int), Component> _previousChildren;
    private readonly Dictionary<ObjectKey, EditContext> _previousEditContexts;

    private readonly Component _root;
    private readonly Stack<string> _scopeStack = new();
    private int _position;

    private LiveRenderContext(
        Component root,
        Dictionary<(Type, int), Component> previousChildren,
        Dictionary<ObjectKey, EditContext> previousEditContexts,
        IServiceProvider? services)
    {
        _root = root;
        _previousChildren = previousChildren;
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

    internal IEnumerable<Component> RenderedComponents => _currentChildren.Values;

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

    public static LiveRenderContext Begin(Component root) =>
        new(root, new Dictionary<(Type, int), Component>(), new Dictionary<ObjectKey, EditContext>(), null);

    public static LiveRenderContext Begin(Component root, IServiceProvider? services) =>
        new(root, new Dictionary<(Type, int), Component>(), new Dictionary<ObjectKey, EditContext>(), services);

    internal static LiveRenderContext Begin(
        Component root,
        Dictionary<(Type, int), Component> previousChildren,
        Dictionary<ObjectKey, EditContext> previousEditContexts,
        IServiceProvider? services) =>
        new(root, previousChildren, previousEditContexts, services);

    internal static LiveRenderContext Begin(
        Component root,
        Dictionary<(Type, int), Component> previousChildren,
        IServiceProvider? services) =>
        new(root, previousChildren, new Dictionary<ObjectKey, EditContext>(), services);

    public string RegisterHandler(Delegate handler) => _root.RegisterHandler(handler);

    public T GetOrCreate<T>(Func<IServiceProvider, T> factory) where T : Component
    {
        var key = (typeof(T), _position++);
        T instance;
        if (_previousChildren.TryGetValue(key, out var prev) && prev is T t)
        {
            instance = t;
        }
        else
        {
            if (Services is null)
            {
                throw new InvalidOperationException(
                    $"Cannot create component '{typeof(T).Name}': LiveRenderContext has no IServiceProvider. " +
                    "Render through MapRask<TApp> or pass a service provider to LiveRenderContext.Begin.");
            }

            instance = factory(Services);
        }

        instance.RenderHandle ??= _handle;
        _currentChildren[key] = instance;
        return instance;
    }

    public Component GetOrCreate(Type type, Func<IServiceProvider, Component> factory)
    {
        var key = (type, _position++);
        Component instance;
        if (_previousChildren.TryGetValue(key, out var prev) && prev.GetType() == type)
        {
            instance = prev;
        }
        else
        {
            if (Services is null)
            {
                throw new InvalidOperationException(
                    $"Cannot create component '{type.Name}': LiveRenderContext has no IServiceProvider. " +
                    "Render through MapRask<TApp> or pass a service provider to LiveRenderContext.Begin.");
            }

            instance = factory(Services);
        }

        instance.RenderHandle ??= _handle;
        _currentChildren[key] = instance;
        return instance;
    }

    internal Dictionary<(Type, int), Component> SnapshotChildren() => _currentChildren;

    public void NotifyParameters(Component component) => component.RaiseLifecycleBeforeRender();

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

    internal readonly struct ObjectKey : IEquatable<ObjectKey>
    {
        public ObjectKey(object value) => Value = value;
        public object Value { get; }
        public bool Equals(ObjectKey other) => ReferenceEquals(Value, other.Value);
        public override bool Equals(object? obj) => obj is ObjectKey k && Equals(k);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(Value);
    }
}
