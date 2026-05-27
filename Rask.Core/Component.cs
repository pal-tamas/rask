using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Core.Components;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Core.ScopedCss;

namespace Rask.Core;

public abstract class Component
{
    // Pre-built "h0".."h255" so handler registration in the common case (small forms,
    // typical pages) doesn't pay a string-concat allocation per call. Overflow above
    // 256 handlers per render falls back to the concat path.
    private static readonly string[] _smallHandlerIds = BuildSmallHandlerIds(1024);
    private HashSet<Component>? _aliveNow;

    // Pooled scratch buffers reused across renders when this component is acting as a
    // render root (only RenderAsLiveRootCore touches them). Lazily allocated on first
    // root render; cleared in place each subsequent frame. Saves three Dictionary/HashSet
    // allocations per render after warmup.
    private HashSet<Component>? _alivePrev;
    private Component? _cachedRenderResult;

    private int _childPositions;

    // Per-component direct children (this component's GetOrCreate calls in its own Render()).
    // Keys are local — position N is the Nth GetOrCreate call inside this component's Render,
    // not the Nth call across the whole tree — so a sibling skipping its render never collides.
    // Lazily allocated: most Elements (Div, Span, …) never call GetOrCreate, so the dict
    // stays null. Pure-HTML render paths (Component.ToHtml() without a live context) never
    // touch _children at all — saves ~80 B per Component instance × O(elements per page).
    private Dictionary<(Type, int), Component>? _children;

    // Sibling pool dict: gets passed as the next frame's `current` so the per-render
    // EditContext dictionary allocation is gone. RenderAsLiveRootCore swaps the two after
    // each render — the just-snapshotted current becomes the next frame's previous, and
    // the now-stale previous gets Clear()ed and reused as next frame's current.
    private Dictionary<LiveRenderContext.ObjectKey, EditContext>? _editContextsPool;
    private Dictionary<string, (Component Owner, Delegate Handler)>? _handlers;
    private bool _hasInitialized;
    private bool _hasRenderedOnce;
    private bool _isUnmounted;
    private CancellationTokenSource? _lifetimeCts;
    private int _nextHandlerId;
    private Dictionary<Component, Component>? _parentMap;

    // Captured by RenderAsLiveRootCore from LiveRenderContext.ScopedJsInvokes BEFORE
    // the ctx is disposed at end of scope. The host's payload builder reads this off
    // the root component after RenderAsLiveRoot returns and emits the scopedJsInvokes
    // JSON field. Per-render — reset at the top of each RenderAsLiveRootCore call.
    // Lazily allocated: only the root of a live render ever writes here; deep tree
    // Elements that never become roots keep this null forever.
    private Dictionary<LiveRenderContext.ObjectKey, EditContext>? _persistedEditContexts;
    private Dictionary<(Type, int), Component>? _previousChildren;

    // Static empty dict for PersistedChildren exposed via the public-internal accessor —
    // saves callers from null checks while keeping the per-instance allocation lazy.
    private static readonly Dictionary<(Type, int), Component> _emptyChildren = new();
    private bool _propsDirty;
    private bool _stateDirty;

    // Set by the children indexer below. Factories no longer expose Children as a
    // parameter — `Div()[Span(...), "hi"]` is the canonical call shape.
    public IEnumerable<Child>? Children { get; set; }

    // `Div()[Span(...), "hi"]` is the only call-site syntax for children. The indexer
    // mutates Children and returns `this` so chained nesting works. The setter remains
    // public so object initializers and direct construction still compile, but every
    // factory call site uses this indexer. Using `params IEnumerable<Child>` so both
    // `Foo()[a, b]` and `Foo()[someEnumerable]` work — the spread operator `..` doesn't
    // apply inside an indexer (parses as `Range`), but a pre-built collection just slots
    // in directly.
    public Component this[params IEnumerable<Child> children]
    {
        get
        {
            Children = children;
            return this;
        }
    }

    // Overload so call sites that build children via LINQ (`items.Select(x => Foo(x))`)
    // don't need a per-item `(Child)` cast — `IEnumerable<Component>` doesn't lift through
    // the user-defined `Component -> Child` conversion. Overload resolution prefers this
    // for Component-typed inputs and falls back to the Child indexer for strings or mixes.
    // Materialise eagerly: the prior `.Select(c => (Child)c)` allocated a Select-iterator
    // state machine that every HtmlSerializer walk had to enumerate, paying a per-render
    // boxed-iterator cost. Walking once at indexer time into a Child[] keeps subsequent
    // foreach loops on a value-type array enumerator with no heap allocation.
    public Component this[params IEnumerable<Component> children]
    {
        get
        {
            if (children is Component[] arr)
            {
                var dst = new Child[arr.Length];
                for (var i = 0; i < arr.Length; i++)
                {
                    dst[i] = arr[i];
                }

                Children = dst;
            }
            else if (children is IReadOnlyCollection<Component> coll)
            {
                var dst = new Child[coll.Count];
                var i = 0;
                foreach (var c in coll)
                {
                    dst[i++] = c;
                }

                Children = dst;
            }
            else
            {
                var list = new List<Child>();
                foreach (var c in children)
                {
                    list.Add(c);
                }

                Children = list;
            }

            return this;
        }
    }

    // Null TagName means "not an HTML element" (Fragment/Doctype/Text/Raw/ErrorBoundary/user
    // components). When non-null, HtmlSerializer wraps WriteAttributes(sb)/RenderChildren()
    // output in `<tag>…</tag>` (or self-closes when SelfClosing is true).
    protected virtual string? TagName => null;
    protected virtual bool SelfClosing => false;

    internal string? TagNameInternal => TagName;
    internal bool SelfClosingInternal => SelfClosing;

    // Nearest enclosing ErrorBoundary, stamped during the render walk (HtmlSerializer
    // default branch). Async lifecycle continuations + dispatcher catch sites consult this
    // pointer to trip the right boundary; null means no ancestor boundary registered.
    internal ErrorBoundary? Boundary { get; set; }

    // Components that read mutable state the framework doesn't observe (e.g. RouteState in
    // Router/Outlet) must opt out of render caching: without this their cached subtree gets
    // reused even after the global state changed. User code should set internal state +
    // call StateHasChanged() instead — only opt in if you genuinely cannot.
    protected virtual bool BypassRenderCache => false;

    /// <summary>
    ///     The current user, resolved from <see cref="IUserProvider" /> in the active render scope.
    ///     Returns an unauthenticated <see cref="ClaimsPrincipal" /> when no provider is registered.
    /// </summary>
    protected ClaimsPrincipal User =>
        LiveRenderContext.Current?.Services?.GetService<IUserProvider>()?.Current
        ?? new ClaimsPrincipal(new ClaimsIdentity());

    /// <summary>
    ///     A <see cref="System.Threading.CancellationToken" /> tied to this component's lifetime.
    ///     Cancelled exactly once when the component is unmounted (navigation away, parent
    ///     removed, or session teardown). Pass into <c>HttpClient</c> calls, <c>Task.Delay</c>,
    ///     or any other cancellable async work started inside a lifecycle hook so it aborts
    ///     cleanly when the component goes away.
    /// </summary>
    protected CancellationToken CancellationToken =>
        LazyInitializer.EnsureInitialized(ref _lifetimeCts, () => new CancellationTokenSource()).Token;

    internal IRenderHandle? RenderHandle { get; set; }

    internal IReadOnlyDictionary<(Type, int), Component> PersistedChildren => _children ?? _emptyChildren;

    /// <summary>
    ///     Override to declare resources this component needs in the page <c>&lt;head&gt;</c>
    ///     (stylesheets, scripts, meta tags, the document title). The framework collects the
    ///     output from every component currently in the tree, dedupes top-level children by
    ///     their rendered HTML, and substitutes the result for the
    ///     <see cref="Generated.RaskHeadAssets" /> placeholder. When a component goes away on
    ///     a subsequent render, its head contribution drops out automatically — the registry
    ///     is rebuilt from scratch each pass.
    ///     <para>
    ///         Default is <c>default</c> — no head contribution. Typical override returns a
    ///         collection expression of <c>Link</c> / <c>Script</c> / <c>Title</c> / <c>Meta</c>
    ///         calls (e.g. <c>Head =&gt; [Title(...), Meta(...)]</c>), a single tag, or a
    ///         <c>Fragment()[...]</c>. Cannot return <c>null</c> (non-nullable value type) — use
    ///         <c>default</c> for "no contribution".
    ///     </para>
    /// </summary>
    protected virtual RenderResult Head => default;

    internal Component? HeadInternal => Head.ToComponentOrNull();

    internal void WriteAttributesInternal(StringBuilder sb) => WriteAttributes(sb);
    internal IEnumerable<Child> RenderChildrenInternal() => RenderChildren();
    internal IDisposable? EnterChildrenScopeInternal() => EnterChildrenScope();

    // Default: no HTML attributes. HTML element subclasses derive from Element, which
    // overrides this to emit id/class/style/data-*. Tag-specific overrides chain via
    // `base.WriteAttributes(sb)` so the universal attrs lead and tag-specific attrs follow.
    // Direct StringBuilder writes avoid the per-attribute KeyValuePair + iterator state-machine
    // allocations that the previous IEnumerable<KVP> shape forced on every render.
    protected virtual void WriteAttributes(StringBuilder sb) { }

    // Emit one attribute with the standard space prefix. Null value → bare attribute
    // (e.g. `required`, `disabled`); non-null → name="encoded-value" with full HTML escaping
    // matching the prior HtmlSerializer behaviour. Fast-paths plain ASCII values through
    // HtmlSerializer.AppendEncoded so encoder-no-op cases skip the allocation.
    protected static void AppendAttr(StringBuilder sb, string name, string? value)
    {
        sb.Append(' ').Append(name);
        if (value is not null)
        {
            sb.Append("=\"");
            HtmlSerializer.AppendEncoded(sb, value);
            sb.Append('"');
        }

        FrameSinkScope.Current?.Attribute(name, value);
    }

    // Overload that writes a two-part attribute name directly without allocating an
    // intermediate concatenation. Used by Element for `data-{key}` — `"data-" + kv.Key`
    // would otherwise allocate a string per data-attribute per render.
    protected static void AppendAttr(StringBuilder sb, string namePrefix, string nameSuffix, string? value)
    {
        sb.Append(' ').Append(namePrefix).Append(nameSuffix);
        if (value is not null)
        {
            sb.Append("=\"");
            HtmlSerializer.AppendEncoded(sb, value);
            sb.Append('"');
        }

        if (FrameSinkScope.Current is { } fw)
        {
            // Only allocate the concatenated name when a frame writer is active. The
            // common no-frames path stays zero-allocation.
            fw.Attribute(namePrefix + nameSuffix, value);
        }
    }

    protected virtual IEnumerable<Child> RenderChildren() => Children ?? [];

    // Tag components override this to wrap children rendering in an ambient scope
    // (e.g. Form pushes an EditContext for descendant fields to consume).
    protected virtual IDisposable? EnterChildrenScope() => null;

    // Test seam: used by ReconciliationTests to inject a "previous render" snapshot
    // for this component before a render begins.
    internal void SeedPreviousChildren(Dictionary<(Type, int), Component> previous) =>
        _previousChildren = previous;

    protected virtual RenderResult Render() => this;

    /// <summary>
    public string ToHtml()
    {
        // Rent a StringBuilder from the shared pool instead of allocating per call. The
        // pool returns it on dispose; oversized buffers (>64 KiB) are discarded so a single
        // huge render doesn't retain an outlier capacity indefinitely.
        var sb = RaskStringBuilderPool.Shared.Get();
        try
        {
            HtmlSerializer.Serialize(this, sb);
            return sb.ToString();
        }
        finally
        {
            RaskStringBuilderPool.Shared.Return(sb);
        }
    }

    protected virtual void OnMount() { }
    protected virtual Task OnMountAsync() => Task.CompletedTask;
    protected virtual void OnPropsChanged() { }
    protected virtual Task OnPropsChangedAsync() => Task.CompletedTask;
    protected virtual void OnRendered(bool firstRender) { }
    protected virtual Task OnRenderedAsync(bool firstRender) => Task.CompletedTask;

    /// <summary>
    ///     Runs once when this component is removed from the tree — navigation away, parent
    ///     subtree torn down, or session disposal. Symmetric with <see cref="OnMount" />.
    ///     The component's <see cref="CancellationToken" /> is still live here; it is
    ///     cancelled immediately after this hook returns. Do not call
    ///     <see cref="StateHasChanged" /> from inside — the component is leaving the tree.
    /// </summary>
    protected virtual void OnUnmount() { }

    /// <summary>
    ///     Async counterpart to <see cref="OnUnmount" />. Awaited on async disposal paths
    ///     (e.g. <c>LiveSession.DisposeAsync</c>), fire-and-forget with fault logging on
    ///     synchronous disposal — mirroring the framework's handling of
    ///     <see cref="IAsyncDisposable" />.
    /// </summary>
    protected virtual Task OnUnmountAsync() => Task.CompletedTask;

    internal void RaiseLifecycleBeforeRender(bool propsChanged)
    {
        var firstRender = !_hasInitialized;
        if (firstRender)
        {
            _hasInitialized = true;
            OnMount();
            InvokeAsyncLifecycleWithRendering(OnMountAsync);
        }

        if (firstRender || propsChanged)
        {
            _propsDirty = true;
            OnPropsChanged();
            InvokeAsyncLifecycleWithRendering(OnPropsChangedAsync);
        }
    }

    internal void RaiseOnRendered(bool publishOnly = false)
    {
        // publishOnly: this is the render walk triggered by a previous OnRenderedAsync
        // continuation's auto-rerender. Skip OnRendered / OnRenderedAsync on components
        // that already rendered at least once — re-entering the hook would re-await
        // whatever it awaits (e.g. js.InvokeVoidAsync), enqueue another pending task,
        // schedule another publish render, complete → loop. First-time renders still
        // fire so newly-mounted components on the same walk get their first
        // OnRendered(firstRender:true) — they don't have a prior continuation in flight,
        // so they can't loop.
        if (publishOnly && _hasRenderedOnce)
        {
            return;
        }

        var firstRender = !_hasRenderedOnce;
        _hasRenderedOnce = true;
        OnRendered(firstRender);

        var task = OnRenderedAsync(firstRender);
        if (task.IsCompleted)
        {
            if (task.IsFaulted)
            {
                ReportLifecycleFault(this, task.Exception);
            }

            return;
        }

        // Auto-rerender on continuation completion so users get OnMountAsync-style
        // "mutate state after the await and it paints" without explicit StateHasChanged.
        // RequestPublishRenderAsync flags the resulting walk as publishOnly so the
        // publish render skips this same hook on every already-rendered component (see
        // top of method). Without that flag, multi-component trees cascade infinitely:
        // A's publish render fires B's OnRenderedAsync, B's continuation publishes,
        // which fires A's OnRenderedAsync again, ad infinitum.
        task.ContinueWith(static (t, state) =>
        {
            var comp = (Component)state!;
            if (t.IsFaulted)
            {
                ReportLifecycleFault(comp, t.Exception);
                return;
            }

            if (t.IsCanceled)
            {
                return;
            }

            if (comp._isUnmounted)
            {
                return;
            }

            var handle = comp.RenderHandle;
            if (handle is null)
            {
                return;
            }

            comp._stateDirty = true;
            _ = handle.RequestPublishRenderAsync();
        }, this, TaskContinuationOptions.ExecuteSynchronously);
    }

    internal void CancelLifetimeToken()
    {
        var cts = Volatile.Read(ref _lifetimeCts);
        if (cts is null)
        {
            return;
        }

        try { cts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    internal void DisposeLifetimeToken()
    {
        var cts = Interlocked.Exchange(ref _lifetimeCts, null);
        cts?.Dispose();
    }

    // Returns null when there's nothing for the caller to await — the async hook either
    // wasn't overridden, completed synchronously, or already failed (faults logged inline).
    // The sync dispose path fire-and-forgets a non-null return via ObserveUnmountFault;
    // the async path awaits it directly. Skipped entirely when _hasInitialized is false —
    // a component that never mounted has no unmount counterpart, symmetric with OnMount.
    internal Task? RaiseUnmount()
    {
        if (!_hasInitialized)
        {
            return null;
        }

        // Set BEFORE OnUnmount fires so any StateHasChanged inside the hook (or
        // from in-flight async work — LifecycleSyncContext continuations from a
        // long-running OnMountAsync — that settles during/after unmount) is
        // silently swallowed instead of queuing ghost session renders against a
        // disposed component. Matches the documented "StateHasChanged() inside
        // OnUnmount is a no-op" contract.
        _isUnmounted = true;

        try { OnUnmount(); }
        catch (Exception ex) { LogUnmountError(this, ex); }

        Task task;
        try { task = OnUnmountAsync(); }
        catch (Exception ex)
        {
            LogUnmountError(this, ex);
            return null;
        }

        if (task.IsCompletedSuccessfully)
        {
            return null;
        }

        if (task.IsFaulted)
        {
            LogUnmountError(this, (Exception?)task.Exception?.InnerException ?? task.Exception!);
            return null;
        }

        if (task.IsCanceled)
        {
            return null;
        }

        return task;
    }

    internal static void LogUnmountError(Component comp, Exception ex) =>
        Console.Error.WriteLine($"Rask unmount hook on {comp.GetType().Name} threw: {ex}");

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
                ReportLifecycleFault(this, task.Exception);
            }

            return;
        }

        // LifecycleSyncContext renders after each in-method await. The terminal render
        // here is the fallback for hooks that return a Task without awaiting it AND for
        // ConfigureAwait(false)-only chains where Post never fires. When the user's last
        // statement IS an await (the common case), Post already fired StateHasChanged
        // for it — and the user's method body returns inside d(state), transitioning the
        // task to Completed while still inside the Post lambda. ExecuteSynchronously
        // would then fire THIS callback inline before Post's own StateHasChanged runs,
        // producing two renders back-to-back. ctx.PostFired lets us short-circuit in
        // that case.
        task.ContinueWith(static (t, state) =>
        {
            var (comp, ctx) = ((Component, LifecycleSyncContext))state!;
            if (t.IsFaulted)
            {
                ReportLifecycleFault(comp, t.Exception);
                return;
            }

            if (t.IsCanceled)
            {
                return;
            }

            if (ctx.PostFired)
            {
                return;
            }

            comp.StateHasChanged();
        }, (this, ctx), TaskContinuationOptions.ExecuteSynchronously);
    }

    private static ErrorBoundary? ResolveHandlerBoundary(Component owner) =>
        owner as ErrorBoundary ?? owner.Boundary;

    private static void ReportLifecycleFault(Component comp, AggregateException? ex)
    {
        var actual = ex?.InnerException ?? ex;
        if (actual is null)
        {
            return;
        }

        // Prefer the boundary: it'll re-render with the fallback. Fall back to Console.Error
        // logging only when there is no ancestor boundary, so a faulting hook is never silent.
        var boundary = comp.Boundary;
        if (boundary is not null)
        {
            boundary.Trip(actual);
            return;
        }

        Console.Error.WriteLine($"Rask lifecycle hook on {comp.GetType().Name} faulted: {actual}");
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

        // Swap the two dictionaries instead of allocating a fresh map per render —
        // both fields persist across the component lifetime, so after first render
        // every subsequent render reuses the same two buffers. _children is cleared
        // before any new writes; _previousChildren retains the prior frame's entries
        // for GetOrCreateChild's reuse lookup. If this component has never had child
        // GetOrCreate calls (most Elements), both dicts stay null and the swap is a
        // no-op — GetOrCreateChild lazily allocates on first write.
        if (_children is not null)
        {
            // First-time swap: allocate the back buffer so the persistent two-dict pool
            // works steady-state. Subsequent renders just swap and Clear — no allocation.
            _previousChildren ??= new Dictionary<(Type, int), Component>();
            (_previousChildren, _children) = (_children, _previousChildren);
            _children.Clear();
        }
        _childPositions = 0;

        // HtmlSerializer wraps every user-component serialization in an EnterParentScope so
        // the scope is live during BOTH Render() and the walk of its returned subtree —
        // factories inside Render and handlers registered on elements deep in the tree both
        // attribute back to this component.
        _cachedRenderResult = Render().ToComponent();

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
        if (_previousChildren is not null && _previousChildren.TryGetValue(key, out var prev) && prev is T t)
        {
            instance = t;
        }
        else
        {
            // Pass through whatever IServiceProvider the LiveRenderContext was given —
            // possibly null. The generated factory closure for non-DI components ignores
            // the parameter, so null is fine; DI-ctor closures (ActivatorUtilities) will
            // surface their own NRE if asked to resolve against a null provider.
            instance = factory(services!);
        }

        instance.RenderHandle ??= handle;
        (_children ??= new Dictionary<(Type, int), Component>())[key] = instance;
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
        if (_previousChildren is not null && _previousChildren.TryGetValue(key, out var prev) && prev.GetType() == type)
        {
            instance = prev;
        }
        else
        {
            instance = factory(services!);
        }

        instance.RenderHandle ??= handle;
        (_children ??= new Dictionary<(Type, int), Component>())[key] = instance;
        return instance;
    }

    private static void ScheduleAsyncContinuation(Component c, Task t, bool rerender)
    {
        if (t.IsCompleted)
        {
            if (t.IsFaulted)
            {
                ReportLifecycleFault(c, t.Exception);
            }

            return;
        }

        t.ContinueWith(static (task, state) =>
        {
            var (comp, doRerender) = ((Component, bool))state!;
            if (task.IsFaulted)
            {
                ReportLifecycleFault(comp, task.Exception);
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
        if (_isUnmounted)
        {
            return;
        }

        _stateDirty = true;
        var handle = RenderHandle;
        if (handle is null)
        {
            return;
        }

        _ = handle.RequestRenderAsync();
    }

    // Internal-only equivalent of StateHasChanged that flips the dirty flag without
    // scheduling a render. RootErrorBoundary uses this to propagate "force the inner
    // root to re-execute Render() this frame" semantics — the same behavior
    // RenderAsLiveRootCore applies to its own root.
    internal void MarkDirtyForFrame() => _stateDirty = true;

    public Task StateHasChangedAsync()
    {
        if (_isUnmounted)
        {
            return Task.CompletedTask;
        }

        _stateDirty = true;
        return RenderHandle?.RequestRenderAsync() ?? Task.CompletedTask;
    }

    internal string RegisterHandler(Delegate handler) =>
        RegisterHandler(handler, this);

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
        var n = _nextHandlerId++;
        var id = n < _smallHandlerIds.Length ? _smallHandlerIds[n] : CreateLargeHandlerId(n);
        _handlers[id] = (owner, handler);
        return id;
    }

    private static string[] BuildSmallHandlerIds(int n)
    {
        var arr = new string[n];
        for (var i = 0; i < n; i++)
        {
            arr[i] = "h" + i;
        }

        return arr;
    }

    // Overflow path for renders with > _smallHandlerIds.Length handlers in one root.
    // The prebake covers 1024 handlers per render — orders of magnitude past anything
    // realistic. When a Virtualize / huge keyed list pushes past that, stackalloc + a
    // direct TryFormat skips the int.ToString allocation that `"h" + n` would force.
    private static string CreateLargeHandlerId(int n)
    {
        Span<char> buf = stackalloc char[12];
        buf[0] = 'h';
        return n.TryFormat(buf[1..], out var written)
            ? new string(buf[..(1 + written)])
            : "h" + n;
    }

    internal ValueTask<bool> TryInvokeHandlerAsync(string id, JsonElement payload)
        => TryInvokeHandlerAsync(id, payload, null);

    internal async ValueTask<bool> TryInvokeHandlerAsync(string id, JsonElement payload, IServiceProvider? services)
    {
        if (_handlers is null || !_handlers.TryGetValue(id, out var entry))
        {
            return false;
        }

        var (owner, handler) = entry;
        using var __dispatchScope = DispatchServicesScope.Push(services);
        // Match Blazor: every event handler implicitly marks the registering component
        // dirty. Set BEFORE running so intermediate renders inside an async handler
        // (via InvokeWithRenderingAsync) already see the owner as dirty.
        owner._stateDirty = true;
        try
        {
            switch (handler)
            {
                case Action a:
                    a();
                    return true;
                case Action<MouseModifiers> am:
                    am(ExtractModifiers(payload));
                    return true;
                case Func<Task> f:
                    await InvokeWithRenderingAsync(f).ConfigureAwait(false);
                    // The mid-await render inside InvokeWithRenderingAsync resets _stateDirty
                    // to false when it walks the owner's subtree. Re-mark dirty here so the
                    // dispatcher's post-handler render picks up state mutated AFTER the
                    // mid-await window (e.g. an async validator's terminal message, or a
                    // user lambda that ran on the continuation of an awaited Task).
                    owner._stateDirty = true;
                    return true;
                case Func<MouseModifiers, Task> fm:
                    var modsForAsync = ExtractModifiers(payload);
                    await InvokeWithRenderingAsync(() => fm(modsForAsync)).ConfigureAwait(false);
                    owner._stateDirty = true;
                    return true;
                case Action<string> a:
                    a(ExtractString(payload, "value"));
                    return true;
                case Func<string, Task> f:
                    var s = ExtractString(payload, "value");
                    await InvokeWithRenderingAsync(() => f(s)).ConfigureAwait(false);
                    owner._stateDirty = true;
                    return true;
                case Action<FormData> a:
                    a(FormData.FromJson(payload));
                    return true;
                case Func<FormData, Task> f:
                    var data = FormData.FromJson(payload);
                    await InvokeWithRenderingAsync(() => f(data)).ConfigureAwait(false);
                    owner._stateDirty = true;
                    return true;
                case Action<ScrollEvent> a:
                    a(ScrollEvent.FromJson(payload));
                    return true;
                case Func<ScrollEvent, Task> f:
                    var scroll = ScrollEvent.FromJson(payload);
                    await InvokeWithRenderingAsync(() => f(scroll)).ConfigureAwait(false);
                    owner._stateDirty = true;
                    return true;
                case Action<IReadOnlyList<RaskFile>> a:
                {
                    var files = FileListReader.Read(payload);
                    try { a(files); }
                    finally { ReleaseFiles(files); }

                    return true;
                }
                case Func<IReadOnlyList<RaskFile>, Task> f:
                {
                    var files = FileListReader.Read(payload);
                    try
                    {
                        await InvokeWithRenderingAsync(() => f(files)).ConfigureAwait(false);
                    }
                    finally { ReleaseFiles(files); }

                    owner._stateDirty = true;
                    return true;
                }
                default:
                    handler.DynamicInvoke();
                    return true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ResolveHandlerBoundary(owner) is not null)
        {
            // Route handler exceptions to the boundary that logically contains the handler.
            // When the owner is itself an ErrorBoundary (the common case: a button rendered
            // directly inside ErrorBoundary's Children — CurrentParent at registration time
            // is the boundary), THAT boundary catches. owner.Boundary would route one level
            // higher. For non-boundary owners (regular components), fall back to their
            // ancestor boundary. Without a boundary the exception bubbles so the dispatcher's
            // catch-and-log still fires.
            ResolveHandlerBoundary(owner)!.Trip(ex);
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
                // Suspend HandlerSyncContext for the duration of the render-and-send. Kestrel's
                // WebSocket.SendAsync has internal awaits that don't all use ConfigureAwait(false),
                // so any leaking ambient sync context becomes the target for its flush
                // continuation. With HandlerSyncContext as the target, that continuation Posts a
                // RunWithRendersAsync, which fires *another* render-and-send on the same socket —
                // a recursive render chain that races the in-flight one, the WS lock, and the
                // user's still-pending async work. Restoring prev for the render call confines
                // HandlerSyncContext to the user-code window where it's actually meaningful.
                SynchronizationContext.SetSynchronizationContext(prev);
                try
                {
                    await handle.RenderInScopeAsync().ConfigureAwait(false);
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(ctx);
                }
            }

            await userTask.ConfigureAwait(false);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prev);
            await ctx.DrainAsync().ConfigureAwait(false);
        }
    }

    internal string RenderAsLiveRoot() => RenderAsLiveRootCore(null, publishOnly: false);

    internal string RenderAsLiveRoot(IServiceProvider services) => RenderAsLiveRootCore(services, publishOnly: false);

    internal string RenderAsLiveRoot(IServiceProvider services, bool publishOnly) =>
        RenderAsLiveRootCore(services, publishOnly);

    private string RenderAsLiveRootCore(IServiceProvider? services, bool publishOnly)
    {
        // Reuse the handler dictionary across renders — IDs are reissued from 0 every
        // root render, so the prior frame's contents are irrelevant. Lazy-init only on
        // the very first render of this component as a root.
        _handlers ??= new Dictionary<string, (Component, Delegate)>();
        _handlers.Clear();
        _nextHandlerId = 0;
        // Lazily init on first root render — non-root Component instances (the 99% case for
        // leaf Elements in a page) never touch this field and stay allocation-free.
        var previousEditContexts = _persistedEditContexts ??= new Dictionary<LiveRenderContext.ObjectKey, EditContext>();
        // Recycle the previously-snapshotted dict as the next frame's `current`. First
        // render: pool is null, allocate once. Steady state: Clear and reuse.
        _editContextsPool ??= new Dictionary<LiveRenderContext.ObjectKey, EditContext>();
        _editContextsPool.Clear();
        using var ctx = LiveRenderContext.Begin(this, previousEditContexts, _editContextsPool, services);

        // Pooled per-frame scratch buffers held on the root component. RenderAsLiveRootCore
        // runs single-threaded per session (the WS dispatcher serializes via the session
        // lock), so reusing these in place is safe and saves three allocations per render
        // after warmup.
        _alivePrev ??= new HashSet<Component>(ReferenceEqualityComparer.Instance);
        _aliveNow ??= new HashSet<Component>(ReferenceEqualityComparer.Instance);
        _parentMap ??= new Dictionary<Component, Component>(ReferenceEqualityComparer.Instance);
        _alivePrev.Clear();
        _aliveNow.Clear();
        _parentMap.Clear();

        // Snapshot the alive set AND parent map BEFORE we touch _children. Walking via
        // every component's _children gives us the same view the previous successful render
        // produced. The parent map is needed in the dispose pass to suppress double-dispose
        // of descendants in a torn-down subtree.
        CollectAliveWithParents(this, _alivePrev, _parentMap);

        // RenderAsLiveRoot is the explicit "render now" entry point — called for the initial
        // GET, WS reconnect recovery render, hot reload, and from tests. Force the root to
        // re-execute Render() this frame; descendants still skip on their own diff. Without
        // this, a second RenderAsLiveRoot call with no descendant marked dirty would skip the
        // root, never re-binding closure-captured state or reading external mutable state.
        _stateDirty = true;
        RaiseLifecycleBeforeRender(false);
        var html = ToHtml();
        // Splice component-declared <head> contributions into the RaskHeadAssets sentinel.
        // The registry was populated by HtmlSerializer as it descended through user
        // components; we resolve the active context (still live before the using-disposal
        // below) and apply once.
        if (LiveRenderContext.Current is { } liveCtx)
        {
            html = liveCtx.HeadAssets.ApplyTo(html, liveCtx.Services);
        }

        // Post-render alive set: union of _children across the whole tree, reachable from root.
        // Components that re-rendered have fresh _children; components that skipped kept theirs.
        CollectAlive(this, _aliveNow);

        foreach (var child in _aliveNow)
        {
            if (!ReferenceEquals(child, this))
            {
                child.RaiseOnRendered(publishOnly);
            }
        }

        RaiseOnRendered(publishOnly);

        // DisposeComponentTree recurses through PersistedChildren — so disposing a parent
        // ALSO disposes its descendants. To avoid disposing each descendant twice, only
        // dispose components whose previously-alive parent is still alive (or whose parent
        // is the root); the parent's recursion will handle the rest.
        foreach (var prev in _alivePrev)
        {
            if (_aliveNow.Contains(prev) || ReferenceEquals(prev, this))
            {
                continue;
            }

            // If our previous parent is also being disposed in this pass, the parent's
            // DisposeComponentTree will cover us — skip to avoid double-dispose.
            if (_parentMap.TryGetValue(prev, out var parent) &&
                !_aliveNow.Contains(parent) &&
                !ReferenceEquals(parent, this))
            {
                continue;
            }

            ComponentLifecycle.DisposeComponentTree(prev);
        }

        // Swap: the dict we wrote into this frame becomes next frame's `previous`;
        // the now-stale previous becomes the pool that next frame will Clear and reuse.
        var snapshot = ctx.SnapshotEditContexts();
        _editContextsPool = _persistedEditContexts;
        _persistedEditContexts = snapshot;
        return html;
    }

    private static void CollectAlive(Component root, HashSet<Component> seen)
    {
        Visit(root, seen);

        static void Visit(Component c, HashSet<Component> seen)
        {
            if (!seen.Add(c))
            {
                return;
            }

            if (c._children is null) return;
            foreach (var child in c._children.Values)
            {
                Visit(child, seen);
            }
        }
    }

    private static void CollectAliveWithParents(
        Component root,
        HashSet<Component> seen,
        Dictionary<Component, Component> parents)
    {
        Visit(root, seen, parents);

        static void Visit(Component c, HashSet<Component> seen, Dictionary<Component, Component> parents)
        {
            if (!seen.Add(c))
            {
                return;
            }

            if (c._children is null) return;
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

    private static MouseModifiers ExtractModifiers(JsonElement payload) =>
        new(ExtractBool(payload, "shiftKey"),
            ExtractBool(payload, "ctrlKey"),
            ExtractBool(payload, "altKey"),
            ExtractBool(payload, "metaKey"));

    private static bool ExtractBool(JsonElement payload, string property)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(property, out var v))
        {
            return false;
        }

        return v.ValueKind == JsonValueKind.True;
    }

    private static void ReleaseFiles(IReadOnlyList<RaskFile> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        FileListReader.ResolveBackend()?.Release(files);
    }
}

public readonly record struct MouseModifiers(bool Shift, bool Ctrl, bool Alt, bool Meta);
