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
    private Dictionary<string, Delegate>? _handlers;
    private bool _hasInitialized;
    private bool _hasRenderedOnce;
    private int _nextHandlerId;
    private Dictionary<(Type, int), Component> _persistedChildren = new();
    private Dictionary<LiveRenderContext.ObjectKey, EditContext> _persistedEditContexts = new();

    protected internal virtual string? Css => null;

    /// <summary>
    ///     The current user, resolved from <see cref="IUserProvider" /> in the active render scope.
    ///     Returns an unauthenticated <see cref="ClaimsPrincipal" /> when no provider is registered.
    /// </summary>
    protected ClaimsPrincipal User =>
        LiveRenderContext.Current?.Services?.GetService<IUserProvider>()?.Current
        ?? new ClaimsPrincipal(new ClaimsIdentity());

    internal IRenderHandle? RenderHandle { get; set; }

    internal IReadOnlyDictionary<(Type, int), Component> PersistedChildren => _persistedChildren;
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
            OnParametersSet();
            InvokeAsyncLifecycleWithRendering(OnParametersSetAsync);
        }
    }

    private void RaiseLifecycleAfterRender()
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

    internal Component RenderForLive() => Render();

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
        var handle = RenderHandle;
        if (handle is null)
        {
            return;
        }

        _ = handle.RequestRenderAsync();
    }

    public Task StateHasChangedAsync() =>
        RenderHandle?.RequestRenderAsync() ?? Task.CompletedTask;

    internal string RegisterHandler(Delegate handler)
    {
        _handlers ??= new Dictionary<string, Delegate>();
        var id = "h" + _nextHandlerId++;
        _handlers[id] = handler;
        return id;
    }

    internal async ValueTask<bool> TryInvokeHandlerAsync(string id, JsonElement payload)
    {
        if (_handlers is null || !_handlers.TryGetValue(id, out var handler))
        {
            return false;
        }

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
        _handlers = new Dictionary<string, Delegate>();
        _nextHandlerId = 0;
        var previousChildren = _persistedChildren;
        var previousEditContexts = _persistedEditContexts;
        using var ctx = LiveRenderContext.Begin(this, previousChildren, previousEditContexts, services);
        RaiseLifecycleBeforeRender(propsChanged: true);
        var html = ToHtml();
        var snapshot = ctx.SnapshotChildren();
        foreach (var child in ctx.RenderedComponents)
        {
            child.RaiseLifecycleAfterRender();
        }

        RaiseLifecycleAfterRender();
        foreach (var (key, prev) in previousChildren)
        {
            if (!snapshot.TryGetValue(key, out var current) || !ReferenceEquals(current, prev))
            {
                ComponentLifecycle.DisposeComponentTree(prev);
            }
        }

        _persistedChildren = snapshot;
        _persistedEditContexts = ctx.SnapshotEditContexts();
        return html;
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
