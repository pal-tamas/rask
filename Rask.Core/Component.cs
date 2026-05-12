using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Core;

public abstract class Component
{
    // Per-component direct children (this component's GetOrCreate calls in its own Render()).
    // Keys are local — position N is the Nth GetOrCreate call inside this component's Render,
    // not the Nth call across the whole tree — so a sibling skipping its render never collides.
    private Dictionary<(Type, int), Component> _children = new();
    private Dictionary<(Type, int), Component> _previousChildren = new();
    private int _childPositions;
    private Component? _cachedRenderResult;
    private Dictionary<string, (Component Owner, Delegate Handler)>? _handlers;
    private bool _hasInitialized;
    private bool _hasRenderedOnce;
    private int _nextHandlerId;
    private Dictionary<LiveRenderContext.ObjectKey, EditContext> _persistedEditContexts = new();
    private bool _propsDirty;
    private bool _stateDirty;

    protected internal virtual string? Css => null;

    // Components that read mutable state the framework doesn't observe (e.g. RouteState in
    // Router/Outlet) must opt out of render caching: without this their cached subtree gets
    // reused even after the global state changed. User code should set internal state +
    // call StateHasChanged() instead — only opt in if you genuinely cannot.
    protected internal virtual bool BypassRenderCache => false;

    /// <summary>
    ///     The current user, resolved from <see cref="IUserProvider" /> in the active render scope.
    ///     Returns an unauthenticated <see cref="ClaimsPrincipal" /> when no provider is registered.
    /// </summary>
    protected ClaimsPrincipal User =>
        LiveRenderContext.Current?.Services?.GetService<IUserProvider>()?.Current
        ?? new ClaimsPrincipal(new ClaimsIdentity());

    internal IRenderHandle? RenderHandle { get; set; }

    internal IReadOnlyDictionary<(Type, int), Component> PersistedChildren => _children;

    // Test seam: used by ReconciliationTests to inject a "previous render" snapshot
    // for this component before a render begins.
    internal void SeedPreviousChildren(Dictionary<(Type, int), Component> previous) =>
        _previousChildren = previous;

    protected abstract Component Render();

    public string ToHtml()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(this, sb);
        return sb.ToString();
    }

    protected virtual void OnInitialized() { }
    protected virtual Task OnInitializedAsync() => Task.CompletedTask;
    protected virtual void OnParametersSet() { }
    protected virtual Task OnParametersSetAsync() => Task.CompletedTask;
    protected virtual void OnAfterRender(bool firstRender) { }
    protected virtual Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;

    internal void RaiseLifecycleBeforeRender(bool propsChanged)
    {
        var firstRender = !_hasInitialized;
        if (firstRender)
        {
            _hasInitialized = true;
            OnInitialized();
            InvokeAsyncLifecycleWithRendering(OnInitializedAsync);
        }

        if (firstRender || propsChanged)
        {
            _propsDirty = true;
            OnParametersSet();
            InvokeAsyncLifecycleWithRendering(OnParametersSetAsync);
        }
    }

    internal void RaiseLifecycleAfterRender()
    {
        var firstRender = !_hasRenderedOnce;
        _hasRenderedOnce = true;
        OnAfterRender(firstRender);
        ScheduleAsyncContinuation(this, OnAfterRenderAsync(firstRender), false);
    }

    private void InvokeAsyncLifecycleWithRendering(Func<Task> invoke)
    {
        var prev = SynchronizationContext.Current;
        var ctx = new LifecycleSyncContext(this);
        SynchronizationContext.SetSynchronizationContext(ctx);
        Task task;
        try { task = invoke(); }
        finally { SynchronizationContext.SetSynchronizationContext(prev); }

        if (task.IsCompleted)
        {
            if (task.IsFaulted)
            {
                Console.Error.WriteLine($"Rask lifecycle hook on {GetType().Name} faulted: {task.Exception}");
            }

            return;
        }

        // LifecycleSyncContext renders after each in-method await. Also re-render once on
        // terminal completion: handles user code that returns a Task without awaiting it.
        task.ContinueWith(static (t, state) =>
        {
            var comp = (Component)state!;
            if (t.IsFaulted)
            {
                Console.Error.WriteLine($"Rask lifecycle hook on {comp.GetType().Name} faulted: {t.Exception}");
                return;
            }

            if (t.IsCanceled)
            {
                return;
            }

            comp.StateHasChanged();
        }, this, TaskContinuationOptions.ExecuteSynchronously);
    }

    internal Component RenderForLive()
    {
        // Skip when nothing meaningful changed: no first-time render, no prop change, no
        // explicit StateHasChanged, and the component hasn't opted out of caching. The
        // serializer still walks _cachedRenderResult, so any descendant whose own
        // _stateDirty or _propsDirty IS set will re-render itself — ancestors don't need to
        // re-execute to permit that.
        if (_cachedRenderResult is not null && !_propsDirty && !_stateDirty && !BypassRenderCache)
        {
            return _cachedRenderResult;
        }

        _previousChildren = _children;
        _children = new Dictionary<(Type, int), Component>();
        _childPositions = 0;

        // HtmlSerializer wraps every user-component serialization in an EnterParentScope so
        // the scope is live during BOTH Render() and the walk of its returned subtree —
        // factories inside Render and handlers registered on elements deep in the tree both
        // attribute back to this component.
        _cachedRenderResult = Render();

        _propsDirty = false;
        _stateDirty = false;
        return _cachedRenderResult;
    }

    internal T GetOrCreateChild<T>(
        Func<IServiceProvider, T> factory,
        IServiceProvider? services,
        IRenderHandle? handle) where T : Component
    {
        var key = (typeof(T), _childPositions++);
        T instance;
        if (_previousChildren.TryGetValue(key, out var prev) && prev is T t)
        {
            instance = t;
        }
        else
        {
            if (services is null)
            {
                throw new InvalidOperationException(
                    $"Cannot create component '{typeof(T).Name}': LiveRenderContext has no IServiceProvider. " +
                    "Render through MapRask<TApp> or pass a service provider to LiveRenderContext.Begin.");
            }

            instance = factory(services);
        }

        instance.RenderHandle ??= handle;
        _children[key] = instance;
        return instance;
    }

    internal Component GetOrCreateChild(
        Type type,
        Func<IServiceProvider, Component> factory,
        IServiceProvider? services,
        IRenderHandle? handle)
    {
        var key = (type, _childPositions++);
        Component instance;
        if (_previousChildren.TryGetValue(key, out var prev) && prev.GetType() == type)
        {
            instance = prev;
        }
        else
        {
            if (services is null)
            {
                throw new InvalidOperationException(
                    $"Cannot create component '{type.Name}': LiveRenderContext has no IServiceProvider. " +
                    "Render through MapRask<TApp> or pass a service provider to LiveRenderContext.Begin.");
            }

            instance = factory(services);
        }

        instance.RenderHandle ??= handle;
        _children[key] = instance;
        return instance;
    }

    private static void ScheduleAsyncContinuation(Component c, Task t, bool rerender)
    {
        if (t.IsCompleted)
        {
            if (t.IsFaulted)
            {
                Console.Error.WriteLine($"Rask lifecycle hook on {c.GetType().Name} faulted: {t.Exception}");
            }

            return;
        }

        t.ContinueWith(static (task, state) =>
        {
            var (comp, doRerender) = ((Component, bool))state!;
            if (task.IsFaulted)
            {
                Console.Error.WriteLine($"Rask lifecycle hook on {comp.GetType().Name} faulted: {task.Exception}");
                return;
            }

            if (task.IsCanceled)
            {
                return;
            }

            if (doRerender)
            {
                comp.StateHasChanged();
            }
        }, (c, rerender), TaskContinuationOptions.ExecuteSynchronously);
    }

    public void StateHasChanged()
    {
        _stateDirty = true;
        var handle = RenderHandle;
        if (handle is null)
        {
            return;
        }

        _ = handle.RequestRenderAsync();
    }

    public Task StateHasChangedAsync()
    {
        _stateDirty = true;
        return RenderHandle?.RequestRenderAsync() ?? Task.CompletedTask;
    }

    internal string RegisterHandler(Delegate handler) =>
        RegisterHandler(handler, owner: this);

    internal string RegisterHandler(Delegate handler, Component owner)
    {
        // For lambdas / method groups that close over `this` inside a Component subclass
        // (e.g., `() => _field++` or `OnSubmit: SubmitHandler`), the delegate's Target is
        // the originating component. That's the right owner to dirty-mark after
        // invocation — it sidesteps the case where an element with a handler is built in
        // ComponentA.Render() but rendered inside ComponentB's subtree (passed as a prop).
        if (handler.Target is Component target)
        {
            owner = target;
        }

        _handlers ??= new Dictionary<string, (Component, Delegate)>();
        var id = "h" + _nextHandlerId++;
        _handlers[id] = (owner, handler);
        return id;
    }

    internal async ValueTask<bool> TryInvokeHandlerAsync(string id, JsonElement payload)
    {
        if (_handlers is null || !_handlers.TryGetValue(id, out var entry))
        {
            return false;
        }

        var (owner, handler) = entry;
        // Match Blazor: every event handler implicitly marks the registering component
        // dirty. Set BEFORE running so intermediate renders inside an async handler
        // (via InvokeWithRenderingAsync) already see the owner as dirty.
        owner._stateDirty = true;
        switch (handler)
        {
            case Action a:
                a();
                return true;
            case Func<Task> f:
                await InvokeWithRenderingAsync(f).ConfigureAwait(false);
                return true;
            case Action<string> a:
                a(ExtractString(payload, "value"));
                return true;
            case Func<string, Task> f:
                var s = ExtractString(payload, "value");
                await InvokeWithRenderingAsync(() => f(s)).ConfigureAwait(false);
                return true;
            case Action<FormData> a:
                a(FormData.FromJson(payload));
                return true;
            case Func<FormData, Task> f:
                var data = FormData.FromJson(payload);
                await InvokeWithRenderingAsync(() => f(data)).ConfigureAwait(false);
                return true;
            default:
                handler.DynamicInvoke();
                return true;
        }
    }

    private async Task InvokeWithRenderingAsync(Func<Task> invoke)
    {
        var handle = RenderHandle;
        if (handle is null)
        {
            await invoke().ConfigureAwait(false);
            return;
        }

        var prev = SynchronizationContext.Current;
        var ctx = new HandlerSyncContext(handle.RenderInScopeAsync);
        SynchronizationContext.SetSynchronizationContext(ctx);
        try
        {
            var userTask = invoke();
            if (!userTask.IsCompleted)
            {
                await handle.RenderInScopeAsync().ConfigureAwait(false);
            }

            await userTask.ConfigureAwait(false);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prev);
            await ctx.DrainAsync().ConfigureAwait(false);
        }
    }

    internal string RenderAsLiveRoot() => RenderAsLiveRootCore(null);

    internal string RenderAsLiveRoot(IServiceProvider services) => RenderAsLiveRootCore(services);

    private string RenderAsLiveRootCore(IServiceProvider? services)
    {
        _handlers = new Dictionary<string, (Component, Delegate)>();
        _nextHandlerId = 0;
        var previousEditContexts = _persistedEditContexts;
        using var ctx = LiveRenderContext.Begin(this, previousEditContexts, services);

        // Snapshot the alive set AND parent map BEFORE we touch _children. Walking via
        // every component's _children gives us the same view the previous successful render
        // produced. The parent map is needed in the dispose pass to suppress double-dispose
        // of descendants in a torn-down subtree.
        var previousParents = new Dictionary<Component, Component>(ReferenceEqualityComparer.Instance);
        var previouslyAlive = CollectAliveWithParents(this, previousParents);

        // RenderAsLiveRoot is the explicit "render now" entry point — called for the initial
        // GET, WS reconnect recovery render, hot reload, and from tests. Force the root to
        // re-execute Render() this frame; descendants still skip on their own diff. Without
        // this, a second RenderAsLiveRoot call with no descendant marked dirty would skip the
        // root, never re-binding closure-captured state or reading external mutable state.
        _stateDirty = true;
        RaiseLifecycleBeforeRender(propsChanged: false);
        var html = ToHtml();

        // Post-render alive set: union of _children across the whole tree, reachable from root.
        // Components that re-rendered have fresh _children; components that skipped kept theirs.
        var nowAlive = CollectAlive(this);

        foreach (var child in nowAlive)
        {
            if (!ReferenceEquals(child, this))
            {
                child.RaiseLifecycleAfterRender();
            }
        }

        RaiseLifecycleAfterRender();

        // DisposeComponentTree recurses through PersistedChildren — so disposing a parent
        // ALSO disposes its descendants. To avoid disposing each descendant twice, only
        // dispose components whose previously-alive parent is still alive (or whose parent
        // is the root); the parent's recursion will handle the rest.
        foreach (var prev in previouslyAlive)
        {
            if (nowAlive.Contains(prev) || ReferenceEquals(prev, this))
            {
                continue;
            }

            // If our previous parent is also being disposed in this pass, the parent's
            // DisposeComponentTree will cover us — skip to avoid double-dispose.
            if (previousParents.TryGetValue(prev, out var parent) &&
                !nowAlive.Contains(parent) &&
                !ReferenceEquals(parent, this))
            {
                continue;
            }

            ComponentLifecycle.DisposeComponentTree(prev);
        }

        _persistedEditContexts = ctx.SnapshotEditContexts();
        return html;
    }

    private static HashSet<Component> CollectAlive(Component root)
    {
        var set = new HashSet<Component>(ReferenceEqualityComparer.Instance);
        Visit(root, set);
        return set;

        static void Visit(Component c, HashSet<Component> seen)
        {
            if (!seen.Add(c))
            {
                return;
            }

            foreach (var child in c._children.Values)
            {
                Visit(child, seen);
            }
        }
    }

    private static HashSet<Component> CollectAliveWithParents(
        Component root,
        Dictionary<Component, Component> parents)
    {
        var set = new HashSet<Component>(ReferenceEqualityComparer.Instance);
        Visit(root, set, parents);
        return set;

        static void Visit(Component c, HashSet<Component> seen, Dictionary<Component, Component> parents)
        {
            if (!seen.Add(c))
            {
                return;
            }

            foreach (var child in c._children.Values)
            {
                parents[child] = c;
                Visit(child, seen, parents);
            }
        }
    }

    private static string ExtractString(JsonElement payload, string property)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(property, out var v))
        {
            return string.Empty;
        }

        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
    }

    public abstract record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
    {
        public virtual IEnumerable<KeyValuePair<string, string?>> ToAttributes()
        {
            if (Id is not null)
            {
                yield return new KeyValuePair<string, string?>("id", Id);
            }

            if (Class is not null)
            {
                yield return new KeyValuePair<string, string?>("class", Class);
            }

            if (Style is not null)
            {
                yield return new KeyValuePair<string, string?>("style", Style);
            }

            if (Data is null)
            {
                yield break;
            }

            foreach (var kv in Data)
            {
                yield return new KeyValuePair<string, string?>($"data-{kv.Key}", kv.Value);
            }
        }
    }
}

internal interface IElement
{
    string TagNameInternal { get; }
    bool SelfClosingInternal { get; }
    IEnumerable<Child> ChildrenInternal { get; }
    IEnumerable<KeyValuePair<string, string?>> AttributesInternal();
    IDisposable? EnterChildrenScope();
}

public abstract class Component<TProps>(TProps? props, IEnumerable<Child>? children) : Component, IElement
    where TProps : Component.Props
{
    protected abstract string TagName { get; }
    protected virtual bool SelfClosing => false;

    internal TProps? PropsInternal => props;
    internal IEnumerable<Child> ChildrenInternal { get; } = children ?? [];

    string IElement.TagNameInternal => TagName;
    bool IElement.SelfClosingInternal => SelfClosing;

    IEnumerable<KeyValuePair<string, string?>> IElement.AttributesInternal() =>
        props is null ? [] : props.ToAttributes();

    IEnumerable<Child> IElement.ChildrenInternal => RenderChildren();
    IDisposable? IElement.EnterChildrenScope() => EnterChildrenScope();

    protected virtual IDisposable? EnterChildrenScope() => null;

    protected virtual IEnumerable<Child> RenderChildren() => ChildrenInternal;

    protected override Component Render() => this;
}
