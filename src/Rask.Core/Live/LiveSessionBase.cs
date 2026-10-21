using System.Buffers;
using System.Reflection.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Core.Diagnostics;
using Rask.Core.Routing;

namespace Rask.Core.Live;

// The transport-independent half of a live session, shared by Rask.Server's LiveSession (WebSocket)
// and Rask.Wasm's WasmLiveSession (in-process JSImport). It owns the render→payload pipeline both
// hosts run identically: render the component tree (capturing the diff frame stream), then decide
// diff-vs-full and write the frame into WriteBuffer. The hosts keep what genuinely differs — their
// transport (WS push vs ApplyRender), their locking, reconnect/dispatch lifecycle, and the dedup
// strategy around the send (Server double-buffers; WASM returns a byte[] each frame).
internal abstract class LiveSessionBase : IRenderHandle, ILiveJsHost, IDisposable
{
    // Pooled across the session lifetime; ResetWrittenCount between frames keeps the rented backing
    // array hot. Non-readonly: TryEmitFrameAsync swaps it with the previous-frame buffer for zero-copy dedup.
    //
    // Sized on demand rather than pre-sized: this is retained per session, and the first payload grows it
    // to the page's real size regardless, so a fixed pre-size only ever mattered for pages small enough
    // not to need it — while costing every concurrent session, including the ones that never send a frame.
    protected ArrayBufferWriter<byte> _writeBuffer = new();

    // The buffer holding the last frame we sent — the double-buffer dedup baseline. TryEmitFrameAsync
    // swaps it with _writeBuffer after each send, so neither the baseline nor the emit copies a byte[].
    protected ArrayBufferWriter<byte>? _lastSentBuffer;

    // Diff-codec state, lazily allocated only when DiffMode opts in — the DisabledFull path pays
    // nothing for these.
    protected List<EditOp>? _diffOps;
    protected SessionRenderCache? _renderCache;

    // Double-buffered rendered-page chars: the current render + the last-applied baseline. Backs the
    // noop-render dedup (suppresses re-morphing identical HTML, which would strip JS-applied DOM state
    // like hljs's `.hljs` class) and the diff-vs-full head-compare, both read-only over the chars — so
    // the page is rendered into a reused buffer instead of a fresh per-update string. Committed (swapped)
    // by the hosts only when a frame is actually sent, mirroring _writeBuffer/_lastSentBuffer.
    protected readonly RenderedHtmlBuffers _htmlBuffers = new();

    /// <summary>
    ///     Tears the session down. Each host owns the order — the Server host returns <see cref="_htmlBuffers" />
    ///     to the pool inside its render lock, so a racing render cannot write into an array another session owns.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>The host's teardown; <paramref name="disposing" /> is always true — sessions have no finalizer.</summary>
    /// <param name="disposing">Whether this is an explicit <see cref="Dispose()" />.</param>
    protected abstract void Dispose(bool disposing);

    // Set when an in-handler StateHasChanged lands mid-dispatch (InHandlerScope=true); the coalescing
    // loop reads and clears it to rebuild the payload before releasing the dispatch lock.
    //
    // Volatile, with InHandlerScope: a server session's background request (a timer, a query or subscription value landing)
    // reads the scope on one thread while the dispatch clears it on another. The request sets this flag and re-reads
    // the scope; the dispatch clears the scope and re-reads this flag. Each write-then-read is only sound when both
    // reads see the other side's write, which is what volatile guarantees.
    protected volatile bool _pendingRenderInScope;

    // The wire-payload shape for THIS session, snapshotted from the host's RaskLiveOptions at
    // construction and read on the render hot path (RenderTreeToHtml / WritePayload) instead of the
    // former process-global LiveOptions.DiffMode static. Per-session so two hosts in one process — and
    // parallel tests — each render in their own mode instead of racing a shared mutable static. Mirrors
    // the per-host RaskServerLimits pattern.
    protected LiveDiffMode DiffMode { get; }

    // The render engine, surfaced to components (Component.HostEngine → LiveRenderContext → here). The
    // IRenderHandle member forwards to a protected virtual so the per-host sessions override with plain
    // virtual dispatch. The base default describes the server host; WasmLiveSession sets Wasm. Constant for
    // the session lifetime.
    RenderEngine IRenderHandle.Engine => EngineCore;

    protected virtual RenderEngine EngineCore => RenderEngine.Server;

    // Whether this session's host runs in Development — its own host's answer, asked once, never another's.
    private bool? _isDevelopment;

    internal bool IsDevelopment => _isDevelopment ??= HostMode.Of(Services) == true;

    bool IRenderHandle.IsDevelopment => IsDevelopment;

    void IRenderHandle.RunInOrder(Func<Task> work) => RunInOrderCore(work);

    // A single-threaded host (WASM) has nothing to order against; the Server session queues on its handler chain.
    protected virtual void RunInOrderCore(Func<Task> work) => _ = work();

    bool IRenderHandle.TryNavigate(Action navigate) => TryNavigateCore(navigate);

    // Each host makes the navigation the way it makes one a link asked for; a session with no host cannot.
    protected virtual bool TryNavigateCore(Action navigate) => false;

    // How Rask DevTools names this session in its feed. Null on a host that has only one session per
    // process (WASM); the Server session answers with its id. A virtual rather than a type test in the
    // devtools, so that package needs no reference to Rask.Server.
    internal virtual string? DevToolsSessionId => null;

    // The gate this session's render walks run under, so Rask DevTools can read what the last walk left behind from
    // another thread without reading it halfway through the next. Null where no such gate exists.
    internal virtual SemaphoreSlim? DevToolsRenderGate => null;

    // The session's culture, read by LiveRenderContext at the top of every render walk. Resolved once in
    // the constructor rather than per walk: the service is scoped to this session, so the instance never
    // changes — only the culture inside it does.
    System.Globalization.CultureInfo IRenderHandle.Culture =>
        _culture?.Culture ?? System.Globalization.CultureInfo.CurrentCulture;

    System.Globalization.CultureInfo IRenderHandle.UICulture =>
        _culture?.UICulture ?? System.Globalization.CultureInfo.CurrentUICulture;

    // Only once the app has actually named a language. An unconfigured culture service reports no
    // supported cultures and sits on the invariant culture, which is not a document language.
    bool IRenderHandle.HasCulture => _culture is { Supported.Count: > 0 };

    // Called at the very start of each render walk (before the tree is serialized). Base is a no-op.
    protected virtual void OnBeforeRenderWalk() { }

    // A language switch has to repaint everything, so the whole tree is marked dirty before re-rendering.
    //
    // Belt AND braces, deliberately. Reading Component.Culture already marks that component as depending
    // on ambient state, which alone would invalidate its cache entry — but only for components that read
    // culture THROUGH Rask. A component formatting with CultureInfo.CurrentCulture directly is invisible
    // to that marking and would otherwise keep serving a cached subtree in the previous language.
    private void OnCultureChanged(object? sender, EventArgs e)
    {
        Component.MarkSubtreeDirtyInternal(View);
        _ = RequestRender();
    }

    // Unhooks the culture subscription. Called by each host's Dispose: the service outlives a
    // per-session tree on WASM (where it is a singleton), so leaving the handler attached would keep a
    // disposed session's tree reachable and re-render it.
    protected void DetachCulture()
    {
        // With the session goes what its controls chose and never wrote.
        _pendingBinds?.Drop();

        if (_culture is not null)
        {
            _culture.Changed -= OnCultureChanged;
        }
    }

    // The session's culture service, or null when the app configured no cultures. Held so the session
    // can unsubscribe on dispose, and so the render walk never pays a service resolution.
    private readonly Globalization.IRaskCulture? _culture;

    // Entered around this session's work so ambient state follows its flow — the tenant of the signed-in
    // user, and the session's IDataChanges. Optional and held per session: the render path resolves nothing,
    // and a host that registers none pays one null check.
    private readonly ISessionWorkScope? _workScope;

    protected LiveSessionBase(Component view, IServiceProvider services, LiveDiffMode diffMode)
    {
        View = view;
        Services = services;
        DiffMode = diffMode;
        view.RenderHandle = this;
        _workScope = services.GetService<ISessionWorkScope>();

        // Only when an app actually configured cultures — otherwise this costs nothing and the whole
        // subsystem stays inert (see RaskCulture.IsEnabled).
        if (Globalization.RaskCulture.IsEnabled
            && services.GetService(typeof(Globalization.IRaskCulture)) is Globalization.IRaskCulture culture)
        {
            _culture = culture;
            _culture.Changed += OnCultureChanged;
        }
        // RootErrorBoundary wraps the user's App; forward the handle to the inner so its
        // StateHasChanged() reaches the session even before the first GetOrCreate would attach it.
        if (view is RootErrorBoundary root)
        {
            root.Inner.RenderHandle = this;
        }

        // Track this session for C# Hot Reload re-render ONLY under `dotnet watch`
        // (MetadataUpdater.IsSupported is a feature switch: false — and constant-folded to dead code the
        // trimmer removes — in a normal/published run). So production pays nothing and the registry never
        // accumulates.
        if (MetadataUpdater.IsSupported)
        {
            RegisterForHotReload();
        }
    }

    // Live sessions tracked weakly so a component-code edit under `dotnet watch` can re-render them; weak
    // refs mean tracking never keeps a session (its DI scope, tree) alive past its normal lifetime, so no
    // explicit unregister is needed — dead entries are pruned while enumerating.
    private static readonly Lock _hotReloadLock = new();
    private static readonly List<WeakReference<LiveSessionBase>> _hotReloadSessions = new();

    // Internal (not ctor-inlined) so tests can register a session without depending on the
    // MetadataUpdater.IsSupported feature switch being on in the test host.
    internal void RegisterForHotReload()
    {
        lock (_hotReloadLock)
        {
            _hotReloadSessions.Add(new WeakReference<LiveSessionBase>(this));
        }
    }

    /// <summary>
    ///     Re-render every tracked live session after a C# Hot Reload apply. Marks each session's whole
    ///     component tree dirty — not just instances of the types the runtime reported as updated, since
    ///     an edit to a helper/static a component calls wouldn't show up there — so every component
    ///     re-executes <c>Render()</c> against the freshly-applied IL, then requests a normal render (a
    ///     diff frame ships over the existing transport). Best-effort and never throws: a faulting session
    ///     is skipped so one bad tree can't stop the rest.
    ///     <para>
    ///         Awaiting the returned task is what lets the coordinator announce "hot reload applied" only
    ///         after the DOM has actually been updated. Only invoked from <c>RaskHotReload</c> under
    ///         <c>dotnet watch</c>.
    ///     </para>
    /// </summary>
    /// <summary>
    ///     Repaints every live session after a hot-reload edit. Returns <c>false</c> when at least one
    ///     session failed to repaint.
    /// </summary>
    /// <remarks>
    ///     Hot reload must never throw — a faulting session cannot be allowed to take down the reload for
    ///     every other one — so a fault is caught per session. But swallowing it and reporting success up
    ///     the stack is what made the coordinator announce "Hot reload applied" for an edit that never
    ///     reached the page: the developer got the green pill while the browser still showed the previous
    ///     render, with the only evidence on the server's stderr (#603). Caught, and reported.
    /// </remarks>
    internal static async Task<bool> RerenderAllForHotReloadAsync()
    {
        List<LiveSessionBase> alive = new();
        lock (_hotReloadLock)
        {
            for (var i = _hotReloadSessions.Count - 1; i >= 0; i--)
            {
                if (_hotReloadSessions[i].TryGetTarget(out var session))
                {
                    alive.Add(session);
                }
                else
                {
                    _hotReloadSessions.RemoveAt(i); // prune a collected session
                }
            }
        }

        var allRepainted = true;
        foreach (var session in alive)
        {
            try
            {
                Component.MarkSubtreeDirtyInternal(session.View);
                await session.RequestRender().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Skip the session — one faulting tree must not stop the others repainting — but say so,
                // and remember it, so the caller does not announce a success that did not happen.
                allRepainted = false;
                RaskDiagnostics.Report(
                    RaskLogLevel.Error,
                    "Rask.HotReload",
                    "A live session failed to repaint after a hot-reload edit; its page still shows the "
                    + "previous render. Reload it to pick the edit up.",
                    ex);
            }
        }

        return allRepainted;
    }

    public Component View { get; }

    // What this session's controls chose and the model has not been told yet (Forms/PendingBinds).
    private Forms.PendingBinds? _pendingBinds;

    Forms.PendingBinds? IRenderHandle.PendingBinds => _pendingBinds ??= new Forms.PendingBinds(View);

    /// <summary>
    ///     Whether the last render actually mounted <paramref name="pageType" />.
    /// </summary>
    /// <remarks>
    ///     The route table alone cannot answer "is the user looking at the not-found page". An app
    ///     whose root renders directly still resolves a route — the fallback is always registered —
    ///     but mounts no <c>Router</c>, so the resolved chain is never rendered and the URL is
    ///     incidental. Asking what the walk actually mounted distinguishes the two without the host
    ///     having to guess whether the app routes: the not-found page is in this set only when the
    ///     user is genuinely looking at it.
    ///     <para>
    ///         Reads the root's per-render mounted-type set, which is cleared at the top of every
    ///         walk and populated unconditionally as components mount, so it describes the render
    ///         whose HTML is about to be served.
    ///     </para>
    /// </remarks>
    internal bool LastRenderMounted(Type pageType) =>
        View.MountedTypeInLastRender(pageType);
    public IServiceProvider Services { get; }

    /// <summary>
    ///     Makes this session's services ambient for the duration of the returned scope.
    /// </summary>
    /// <remarks>
    ///     Every path that runs work for this session brackets itself with this, on both hosts: the render
    ///     entry points and the handler dispatch. Miss one and anything reading ambient session state is wrong
    ///     on that path alone; wrap something that outlives the work and it bleeds into the next session, which
    ///     for a tenant filter means reading somebody else's rows. Nesting is fine — a dispatch renders, so
    ///     this is entered inside itself, and each disposal restores exactly what it replaced.
    /// </remarks>
    internal IDisposable? EnterWorkScope() => _workScope?.Enter(EventServices);

    // Null until something asks for the event's services, and for a host that registered no catalog (a bare test
    // store), where the session's own services answer for every event, as they always did.
    private EventServices? _eventServices;
    private bool _eventServicesResolved;

    /// <summary>
    ///     The services of the event in progress: the app's scoped services fresh for each event, in a scope opened
    ///     on first use and disposed as the event ends; the framework's from the session.
    /// </summary>
    /// <remarks>What is ambient while an event runs — a handler, a render, a navigation — and never what a component is built with: a component outlives every event.</remarks>
    internal IServiceProvider EventServices
    {
        get
        {
            if (!_eventServicesResolved)
            {
                _eventServices = Services.GetService<EventScopeCatalog>() is { } catalog
                                 && Services.GetService<IServiceScopeFactory>() is { } scopes
                    ? new EventServices(this, catalog, scopes)
                    : null;
                _eventServicesResolved = true;
            }

            return (IServiceProvider?)_eventServices ?? Services;
        }
    }

    /// <summary>The scope the event in progress has opened, or null — for tests.</summary>
    internal IServiceProvider? OpenedEventScope => _eventServices?.Opened;

    /// <summary>
    ///     Makes a scope just opened for an event this session's: the services that are the session's
    ///     (<see cref="SessionScoped" />) are answered there with its own instances — its user as revalidated for this
    ///     event, its connection, its route.
    /// </summary>
    /// <param name="eventScope">The event's own services.</param>
    internal void SeedEventScope(IServiceProvider eventScope)
    {
        if (eventScope.GetService<EventScopeLink>() is { } link)
        {
            link.Session = Services;
        }
    }

    /// <summary>Ends the event in progress: the scope it opened is disposed, after the reads its page left behind.</summary>
    internal void EndEvent()
    {
        if (_eventServices is { } events)
        {
            events.Close(Interlocked.Exchange(ref _readsStillRunning, null));
        }
    }

    // Pending IJSRuntime calls, drained into each frame's jsInvokes and dispatched client-side after
    // applyDiff (post-commit ordering). Shared queue type so both hosts order interop identically.
    public LiveJsInvokeQueue JsInvokes { get; } = new();

    // Plain instance bool, NOT AsyncLocal: the dispatch lock is owned by the session as a whole, not
    // by any one async chain. AsyncLocal would flow into Timer/Task captures created during a render
    // and later report InHandlerScope=true forever, stranding background StateHasChanged calls.
    public bool InHandlerScope
    {
        get => _inHandlerScope;
        set
        {
            _inHandlerScope = value;

            // Every host clears it as the event that set it ends, after its last render: that is where the
            // event's scope goes.
            if (!value)
            {
                EndEvent();
            }
        }
    }

    // Volatile for the handoff described at _pendingRenderInScope.
    private volatile bool _inHandlerScope;

    public Task RequestRender() => RequestRenderInternalAsync(false);

    public Task RequestPublishRender() => RequestRenderInternalAsync(true);

    // A StateHasChanged that lands while the scope is held and is not drawn inside it: after the coalescing loop has
    // settled — during the noop guard, the frame emit, the commit — or past the loop's budget. The in-scope branch
    // parks it in _pendingRenderInScope, and the NEXT dispatch's loop opens by clearing the flag, so left there it was
    // discarded, not deferred (#986). This is the dispatch side of that handoff, run after the scope is cleared: the
    // request becomes a render of its own, so the state last written is never left undrawn.
    //
    // A PUBLISH render, not a plain one: a plain render bypasses the noop guard and pushes the frame even when the
    // HTML is byte-identical, forcing the client to morph identical markup and stripping DOM state JS applied since
    // the last frame. It also fires no OnRendered on a component that has rendered before, which is what ends a
    // component that asks for a render from every OnRendered; one that asks from every Render is slowed by RenderPace.
    //
    // afterRequestedRender: the scope that ended was itself a render made for a request, not an event's dispatch —
    // what RenderPace counts a row of.
    internal Task DrainRenderRequestedAfterScope(bool afterRequestedRender = false)
    {
        if (!_pendingRenderInScope)
        {
            return Task.CompletedTask;
        }

        _pendingRenderInScope = false;
        return _pace.Next(DevToolsSessionId, afterRequestedRender) switch
        {
            RenderPace.Turn.Now => RequestPublishRender(),
            RenderPace.Turn.AfterAPause => RenderAfterAPauseAsync(),
            _ => Task.CompletedTask,
        };
    }

    // How the renders made after a scope, for requests it left undrawn, are spaced once they come without end.
    private readonly RenderPace _pace = new();

    private async Task RenderAfterAPauseAsync()
    {
        // Ended early by the page's teardown, after which the request below finds nobody to render for.
        await Task.Delay(RenderPace.Pause, View.LifetimeTokenInternal).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        // Before the render: whatever asks from here on is drawn by a render of its own.
        _pace.PauseOver();
        await RequestPublishRender().ConfigureAwait(false);
    }

    void IRenderHandle.Asked(Component by) => _pace.Asked(by);

    /// <summary>
    ///     Set while a batch of events is being handled and one of them has changed the page without rendering it:
    ///     the render after the batch's last event is the one that shows it. Cleared by any walk of the tree.
    /// </summary>
    internal bool RenderOwed { get; private set; }

    /// <summary>Leaves a handler's render to the one after its batch's last event.</summary>
    /// <remarks>
    ///     A <c>StateHasChanged</c> the handler made asked for a render of its own once the scope ends. That render
    ///     is the owed one, so the request is taken back rather than answered twice.
    /// </remarks>
    internal void OweRender()
    {
        RenderOwed = true;
        _pendingRenderInScope = false;
    }

    /// <summary>Ends a batch: nothing is owed past it, even where its last render was handed to another page.</summary>
    internal void EndBatch() => RenderOwed = false;

    // The tree as last rendered, child to parent. Built for the first event of a batch that asks, and dropped by
    // the next walk, which is the only thing that changes it.
    private Dictionary<Component, Component>? _parents;
    private bool _parentsMapped;

    /// <summary>
    ///     Whether the handler <paramref name="id" /> names may run ahead of the render its batch owes — see
    ///     <see cref="Component.HandlerOutlivesRender(string, System.Text.Json.JsonElement, Dictionary{Component, Component})" />. When it may not, the host renders first.
    /// </summary>
    internal bool HandlerOutlivesRender(string id, System.Text.Json.JsonElement sent)
    {
        _parents ??= new Dictionary<Component, Component>(ReferenceEqualityComparer.Instance);
        if (!_parentsMapped)
        {
            View.MapParents(_parents);
            _parentsMapped = true;
        }

        return View.HandlerOutlivesRender(id, sent, _parents);
    }

    // A walk shows everything the handlers before it did, and may mount or remove anything.
    private void RenderOwedIsThisWalk()
    {
        RenderOwed = false;
        if (_parentsMapped)
        {
            _parents!.Clear();
            _parentsMapped = false;
        }
    }

    Task IRenderHandle.RenderInScopeAsync() => RenderInScopeCoreAsync();

    TimeSpan IRenderHandle.HandlerRepaintBudget => HandlerBudget;

    /// <summary>
    ///     How long a dispatch's page is waited for: the handler's own repaints are withheld for it
    ///     (<see cref="IRenderHandle.HandlerRepaintBudget" />), and the render that follows the handler waits that
    ///     long for the reads it started (<see cref="RenderInWavesAsync" />). Zero for a session built without
    ///     its host's options, which repaints at every await and sends at once.
    /// </summary>
    protected TimeSpan HandlerBudget { get; init; }

    protected abstract Task RequestRenderInternalAsync(bool publishOnly);

    /// <summary>The framework's mid-await intermediate render (see Component.InvokeWithRenderingAsync).</summary>
    protected abstract Task RenderInScopeCoreAsync();

    /// <summary>
    ///     Double-buffered zero-copy send, shared by both hosts. Skips the frame currently in
    ///     <see cref="_writeBuffer" /> when it's byte-identical to the last one sent (dedup), otherwise
    ///     hands its bytes to the host transport via <see cref="SendFrameAsync" /> and swaps the buffers
    ///     so the sent frame becomes the next dedup baseline and the old baseline is recycled as the
    ///     next write target (<see cref="WritePayload" /> resets its <c>WrittenCount</c> before writing).
    ///     <paramref name="force" /> bypasses the dedup for frames that must flow even when the rendered
    ///     output is unchanged (navigation / auth / download). Returns whether a frame was sent.
    /// </summary>
    protected async ValueTask<bool> TryEmitFrameAsync(bool force)
    {
        if (_writeBuffer.WrittenCount == 0)
        {
            View.HandlersRepeated();
            return false;
        }

        // Only a page repeats: a diff is a change to the page the browser has, and the same change twice is two changes.
        // A swap of two neighbours and the swap back are the same ops (#16), and dropping the second lost the move.
        if (!force && !_writeIsDiff && _lastSentBuffer is not null && RepeatsLastSent())
        {
            View.HandlersRepeated();
            return false;
        }

        await SendFrameAsync(_writeBuffer.WrittenMemory).ConfigureAwait(false);

        // The browser has this page now: the number it carried is the one its events will name.
        View.HandlersSent();

        // Before the swap: the frame just sent is still the write buffer.
        if (RaskDevToolsHook.Active is { } devTools)
        {
            devTools.FrameSent(this, _writeBuffer.WrittenCount);
        }

        (_lastSentBuffer, _writeBuffer) = (_writeBuffer, _lastSentBuffer ?? new ArrayBufferWriter<byte>());
        _lastSentVersionBytes = _writeVersionBytes;
        return true;
    }

    // What each buffer's page number takes at its end. A page whose handlers were made again by its render is
    // given the next number, and is still the page the browser has when everything before the number repeats:
    // sending it would morph a document into itself, undoing whatever a script did to it since.
    private int _writeVersionBytes;

    // Whether the write buffer holds a diff rather than a page.
    private bool _writeIsDiff;
    private int _lastSentVersionBytes;

    private bool RepeatsLastSent() =>
        WithoutVersion(_writeBuffer.WrittenSpan, _writeVersionBytes)
            .SequenceEqual(WithoutVersion(_lastSentBuffer!.WrittenSpan, _lastSentVersionBytes));

    // The payload up to its page number: the number is its last field, followed only by the closing brace —
    // which is left out either way, so a payload with a number and one without compare on what they carry.
    private static ReadOnlySpan<byte> WithoutVersion(ReadOnlySpan<byte> payload, int versionBytes) =>
        payload.Length > versionBytes ? payload[..^(versionBytes + 1)] : payload;

    // Says which page the payload built inside the scope is, and remembers how long its number is.
    private LivePayload.PageScope NumberedPage(bool whole)
    {
        var version = View.HandlerVersionToSend(whole);
        _writeVersionBytes = LivePayload.PageVersionBytes(version);
        return LivePayload.BeginPage(version);
    }

    /// <summary>
    ///     Push one built frame's bytes to the host transport: Server writes them to the WebSocket
    ///     (<c>ReadOnlyMemory&lt;byte&gt;</c>, zero-copy); WASM pushes them to JS via a zero-copy
    ///     <c>MemoryView</c>. WASM's is synchronous — it returns a completed <see cref="ValueTask" />.
    /// </summary>
    protected abstract ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame);

    /// <summary>
    ///     Render the component tree to HTML, capturing the parallel <c>RenderFrame</c> stream when
    ///     the diff codec is on (so <see cref="WritePayload" /> can emit a minimal edit-op payload).
    ///     Default (DisabledFull) bypasses entirely — null frame sink, a single null check per
    ///     HtmlSerializer branch.
    /// </summary>
    protected ReadOnlyMemory<char> RenderTreeToHtml(bool publishOnly, out FrameWriter? frameWriter)
    {
        OnBeforeRenderWalk();
        RenderOwedIsThisWalk();
        RaskDevToolsHook.Active?.WalkStarted(this, publishOnly);
        frameWriter = null;
        FrameSinkScope.Popper popper = default;
        if (DiffMode != LiveDiffMode.DisabledFull)
        {
            _renderCache ??= new SessionRenderCache { Development = IsDevelopment };
            frameWriter = _renderCache.PrepareCurrentBuffer();
            popper = FrameSinkScope.Push(frameWriter);
        }

        try
        {
            // Render into the session's reused char buffer (no per-update page string); the caller
            // consumes the chars synchronously before the next render overwrites them.
            View.RenderAsLiveRootInto(Services, publishOnly, _htmlBuffers);
            return _htmlBuffers.Current;
        }
        finally
        {
            if (frameWriter is not null)
            {
                popper.Dispose();
            }
        }
    }

    // The scope a waiting render's walk opens when it starts work, and only then.
    private readonly QuiescenceScope.OnDemand _reads = new();

    // What the last waiting render left unread when its budget ran out. Written under the host's render gate,
    // taken by the dispatch that made the render.
    private Task? _unsettledReads;

    // The same reads without the cap: what the event's scope waits for before it is disposed, since they may still be
    // using what they resolved from it when the browser has long been told the event is done.
    private Task? _readsStillRunning;

    /// <summary>
    ///     The reads the dispatch's page went out without, as one task that completes when they are all back —
    ///     or <see cref="ReadsCap" /> has passed — so whoever tells the browser the dispatch is done can say so
    ///     then. Null when it went out whole.
    /// </summary>
    internal Task? TakeUnsettledReads() => Interlocked.Exchange(ref _unsettledReads, null);

    /// <summary>
    ///     How long after a page went out without its reads the browser is told the event is done anyway: the
    ///     busy mark, the dimmed rows and the bar end then, and a read that never returns does not hold them
    ///     until the client gives up. The reads are not cancelled, and what they bring is a frame of its own.
    /// </summary>
    internal static readonly TimeSpan DefaultReadsCap = TimeSpan.FromSeconds(2);

    /// <summary>The cap this session waits. Settable so a test need not sit out two seconds.</summary>
    internal TimeSpan ReadsCap { get; set; } = DefaultReadsCap;

    /// <summary>
    ///     What an earlier dispatch left unread is not the next one's to report. Called as a dispatch starts: one
    ///     that renders nothing would otherwise answer for the navigation before it.
    /// </summary>
    internal void ForgetUnsettledReads() => Volatile.Write(ref _unsettledReads, null);

    /// <summary>Whether the page just rendered has sent the reader on, so it is neither waited for nor rendered again.</summary>
    protected virtual bool LeavingThePage(bool arriving) => false;

    /// <summary>
    ///     Renders, and renders again as the lifecycle work and the queries that render started come back, until
    ///     none is pending or <paramref name="budget" /> is spent — the first request's waves
    ///     (<see cref="QuiescentRender" />), for a page that is already live.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A render that started nothing returns at once, having opened no scope and armed no timer: this is
    ///         every keystroke's path. Spending the budget cancels nothing — the page goes out as it stands, the
    ///         rest follows as frames of its own, and <see cref="TakeUnsettledReads" /> says when.
    ///     </para>
    ///     <para>The host holds its render gate throughout, so the frame it sends is the last wave's.</para>
    /// </remarks>
    /// <param name="publishOnly">Whether the first render is a publish render. Every later one is.</param>
    /// <param name="budget">One deadline for all the waves.</param>
    /// <param name="arriving">Whether the render carries a navigation, for <see cref="LeavingThePage" />.</param>
    /// <param name="cancellationToken">Ends the wait with <see cref="OperationCanceledException" />.</param>
    protected ValueTask<(ReadOnlyMemory<char> Html, FrameWriter? Frames)> RenderInWavesAsync(
        bool publishOnly, TimeSpan budget, bool arriving, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ReadOnlyMemory<char> html;
        FrameWriter? frames;
        using (QuiescenceScope.Collect(_reads))
        {
            html = RenderTreeToHtml(publishOnly, out frames);
        }

        return _reads.Take() is { } started
            ? FollowTheReadsAsync(started, html, frames, budget, arriving, cancellationToken)
            : new ValueTask<(ReadOnlyMemory<char>, FrameWriter?)>((html, frames));
    }

    private async ValueTask<(ReadOnlyMemory<char> Html, FrameWriter? Frames)> FollowTheReadsAsync(
        QuiescenceScope started,
        ReadOnlyMemory<char> html,
        FrameWriter? frames,
        TimeSpan budget,
        bool arriving,
        CancellationToken cancellationToken)
    {
        try
        {
            await QuiescentRender.Waves(
                started,
                string.Empty,
                _ =>
                {
                    // A page that has sent the reader on is not rendered again: its destination is next.
                    if (!LeavingThePage(arriving))
                    {
                        using (QuiescenceScope.Enter(started))
                        {
                            html = RenderTreeToHtml(publishOnly: true, out frames);
                        }
                    }

                    return string.Empty;
                },
                budget,
                // Work waiting on the browser cannot finish before a frame reaches it, and a page that
                // redirected is not waited for at all.
                isBlocked: () => JsInvokes.HasPending || LeavingThePage(arriving),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            started.Dispose();
            throw;
        }

        if (started.Unsettled is { } left)
        {
            // The scope stays open until they are back: what their continuations start is theirs too.
            var settled = SettledAsync(started, left);
            Volatile.Write(ref _readsStillRunning, settled);
            Volatile.Write(ref _unsettledReads, WithinTheCapAsync(settled, ReadsCap));
        }
        else
        {
            started.Dispose();
        }

        return (html, frames);
    }

    // The reads, or the cap, whichever is first. The timer does not outlive reads that came back in time.
    private static async Task WithinTheCapAsync(Task settled, TimeSpan cap)
    {
        using var stop = new CancellationTokenSource();
        await Task.WhenAny(settled, Task.Delay(cap, stop.Token)).ConfigureAwait(false);
        await stop.CancelAsync().ConfigureAwait(false);
    }

    // Completes when the reads a page went out without are back AND the repaint each asked for has been
    // requested. The hook's own task ends one statement before that request (#932), so the scope is asked
    // again for the gate its continuation registered, a bounded number of times.
    private static async Task SettledAsync(QuiescenceScope started, Task left)
    {
        using (started)
        {
            await left.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            for (var round = 0; round < QuiescentRender.DefaultMaxWaves && started.TrySnapshotPending(out var more); round++)
            {
                await Task.WhenAll(more).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    /// <summary>Consume a one-shot download side-effect produced during the render, if any.</summary>
    protected PendingDownload? ConsumeDownload() =>
        Services.GetService<IDownloadSink>() is { } sink && sink.TryConsume(out var pd) ? pd : null;

    /// <summary>
    ///     A sealed session-resume record to attach to the payload being written, or <c>null</c> for none.
    /// </summary>
    /// <remarks>
    ///     Only the ASP.NET host overrides this. A WASM app IS the process holding its session, so there is
    ///     no other host for a record to be redeemed on and nothing to carry — the base returns null and
    ///     that host pays a null check per frame. Named "Take" because an override is expected to
    ///     mark the record as issued: it is called once per payload and must not hand out the same record
    ///     on every subsequent frame.
    /// </remarks>
    protected virtual string? TakeResumeToken() => null;

    /// <summary>Bytes the <c>"resume":"…"</c> field name, quotes and separator add around the record itself.</summary>
    private const int ResumeFieldOverhead = 12;

    /// <summary>Bytes the <c>devError</c> object's own field names, braces, quotes and separators add.</summary>
    private const int DevErrorFieldOverhead = 64;

    private DevErrorInfo? _pendingDevError;

    /// <inheritdoc cref="IRenderHandle.ReportDevError" />
    void IRenderHandle.ReportDevError(DevErrorInfo error) => _pendingDevError = error;

    /// <summary>
    ///     True when the walk just finished recorded a development fault. Read by the host's HTML-level
    ///     dedup, which would otherwise drop the whole frame: a handler that threw and changed nothing
    ///     renders byte-identical HTML, so the overlay would never reach the browser precisely in the
    ///     simplest case — a click whose only effect was the exception.
    /// </summary>
    protected bool HasPendingDevError => _pendingDevError is not null;

    /// <summary>
    ///     Takes the development error the render walk recorded, if any, clearing it so the overlay is
    ///     shown once rather than on every subsequent frame.
    /// </summary>
    private DevErrorInfo? TakeDevError()
    {
        var error = _pendingDevError;
        _pendingDevError = null;
        return error;
    }

    private static int DevErrorCost(DevErrorInfo error) =>
        error.Kind.Length + error.Title.Length + error.Message.Length + error.Detail.Length
        + DevErrorFieldOverhead;

    /// <summary>
    ///     Decide diff-vs-full for the just-rendered <paramref name="html" /> and write the frame
    ///     into <see cref="_writeBuffer" />. Identical on both hosts; the seams are parameters:
    ///     <paramref name="auth" /> (Server only — gates the diff path and rides the full payload;
    ///     WASM passes null), <paramref name="sessionId" /> (the data-rask-root value), and
    ///     <paramref name="commitCache" /> (false during a coalescing loop, so intermediate rebuilds
    ///     diff against the stable last-sent baseline and the caller Snapshots once after).
    /// </summary>
    protected void WritePayload(ReadOnlyMemory<char> html, FrameWriter? frameWriter, PendingDownload? download,
        PendingJsInvoke[]? jsInvokes, string? historyUrl, bool replace, bool commitCache,
        AuthInstruction? auth, string sessionId)
    {
        _writeBuffer.ResetWrittenCount();
        _writeVersionBytes = 0;

        // A session-resume record, when this host issues them and the page has moved since the last one.
        // Taken once per payload — before either branch below — so the diff and full paths carry the same
        // record and a frame that ends up discarded on size doesn't consume it twice. Null on every host
        // but the ASP.NET one, which is the only place a session can outlive the process holding it.
        var resume = TakeResumeToken();

        // A development error to paint over the app, recorded by RootErrorBoundary during the walk that
        // produced `html`. Taken once, for the same reason the resume record is: both paths must carry
        // the same one, and a frame discarded on size must not consume it.
        var devError = TakeDevError();

        // Out-of-band side effects (auth, download) and structural ops route to the full-HTML morph
        // path; in-place state changes ship a diff. Head changes (per-route <title>, a scoped-asset
        // <link>, a reactive title) ride the diff as an attached <head> fragment since the diff
        // frame stream never carries <head>. Genuine body-restructuring page swaps fall back to full
        // HTML via DiffOpsAreClientSupported. jsInvokes do NOT force full HTML — they ride the diff.
        // Timed from here, so the devtools see the diff-or-full decision and the payload build as one cost.
        var devTools = RaskDevToolsHook.Active;
        var devToolsStart = devTools is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();

        var diffCache = frameWriter is not null && auth is null && download is null ? _renderCache : null;
        var diffPathEntered = diffCache is not null;
        var usedDiff = _writeIsDiff = diffCache is not null
                       && TryWriteDiff(diffCache, html, jsInvokes, historyUrl, replace, commitCache, resume, devError);

        // A render with a sign-in handoff or a download never reaches the differ, so the branch that names a
        // refused diff never sees it. In Development it is named here: no whole page goes out unexplained.
        if (diffCache is null && frameWriter is not null && IsDevelopment)
        {
            FullPageReply.ReportOutOfBand(auth is not null, html.Length);
        }

        if (!usedDiff)
        {
            // Full-HTML path (first render, structural change, out-of-band side effect, size fallback):
            // encoded straight from the pooled page buffer — a page-sized string here would be garbage
            // the moment the payload is written, and on the large-object heap past ~42K chars.
            using (NumberedPage(whole: true))
            {
                LivePayload.BuildPayloadUtf8WithRoot(_writeBuffer, html.Span, sessionId, historyUrl,
                    replace, auth, download, jsInvokes, resume, devError);
            }

            // Keep the cache in lockstep with the client even when shipping full HTML: promote
            // current → previous so the NEXT diff's baseline matches what the client received. Skip
            // when TryComputeDiff already rotated (diffPathEntered), or when the caller defers the
            // commit to the coalescing loop (commitCache=false).
            if (!diffPathEntered && commitCache)
            {
                _renderCache?.Snapshot();
            }
        }

        // Ops only count when they shipped: a diff that lost on size was rebuilt as full HTML above.
        devTools?.DiffComputed(this, usedDiff ? _diffOps!.Count : 0, usedDiff, devToolsStart);
    }

    // The tree the browser holds is the tree this render drew: the markup may still differ, but only in what a tree
    // does not describe — the order two attributes are written in, say — so there is nothing to patch and nothing to
    // send. A page is not the answer: it would land on whatever the reader has typed since. Not when the markup
    // baseline was dropped (a reconnect's catch-up is a page by design), nor for a level the differ gave up on.
    private bool TreeUnchanged(SessionRenderCache renderCache) =>
        _diffOps!.Count == 0 && _htmlBuffers.HasPrevious && !renderCache.LastDiffForcedFullHtml;

    // The diff path: builds the diff frame into the write buffer and reports whether the render is answered. A diff
    // that cannot carry the change, or that loses to the full body on size, leaves the buffer empty and returns false;
    // a render with nothing to say leaves it empty and returns true: the answer is no frame at all.
    private bool TryWriteDiff(SessionRenderCache renderCache, ReadOnlyMemory<char> html, PendingJsInvoke[]? jsInvokes,
        string? historyUrl, bool replace, bool commitCache, string? resume, DevErrorInfo? devError)
    {
        _diffOps ??= new List<EditOp>();
        var headChanged = _htmlBuffers.HasPrevious && !LiveDiffGate.HeadUnchanged(html.Span, _htmlBuffers.PreviousSpan);
        // Ship the diff when it carries DOM ops, OR when it carries none but a navigation, a head
        // change, a script call or a development error must still flow: a query-only nav pushes the URL,
        // a head-only change ships the head fragment, a handler that only called script changed nothing
        // else and the call used to cost the whole page. Zero ops and none of those means nothing to send.
        var compared = renderCache.TryComputeDiff(_diffOps, commitCache, html.Span);
        var carries = historyUrl is not null || headChanged || jsInvokes is not null || devError is not null;
        if (compared && !carries && TreeUnchanged(renderCache))
        {
            return true;
        }

        var diffBytes = 0;
        if (compared
            && (_diffOps.Count > 0 || carries)
            && LiveDiffGate.DiffOpsAreClientSupported(_diffOps)
            && !renderCache.LastDiffForcedFullHtml)
        {
            var headHtml = headChanged ? LiveDiffGate.ExtractHead(html.Span) : null;
            using (NumberedPage(whole: false))
            {
                LivePayload.BuildPayloadUtf8Diff(_writeBuffer, _diffOps, historyUrl, replace, jsInvokes,
                    headHtml, html.Span, resume, devError);
            }

            // Ship the diff whenever it isn't larger than re-sending the body, or unconditionally
            // under Forced. Only the pathological case (nearly every node changed on a tiny page,
            // so op-list framing exceeds the body) falls back to full HTML on size.
            //
            // The resume record is discounted from the diff's measured size because the full-HTML
            // payload would carry the identical record: it is on both sides of this comparison, so
            // letting it count only against the diff would flip small pages to full HTML purely
            // because a record happened to be due — a page whose diff is a few hundred bytes would
            // start shipping its whole body every time the declared state moved.
            // The dev-error record is discounted for exactly the reason the resume record is: the
            // full-HTML payload carries the identical record, so it sits on both sides of this
            // comparison. Letting it count only against the diff would flip a page to full HTML
            // purely because a handler threw — shipping the whole body at the moment the developer
            // most wants a minimal, legible frame.
            var resumeCost = resume is null ? 0 : resume.Length + ResumeFieldOverhead;
            var devErrorCost = devError is null ? 0 : DevErrorCost(devError);
            if (DiffMode == LiveDiffMode.Forced
                || _writeBuffer.WrittenCount - resumeCost - devErrorCost < html.Length)
            {
                return true;
            }

            diffBytes = _writeBuffer.WrittenCount;
            _writeBuffer.ResetWrittenCount();
        }

        // The whole page goes out instead. In Development, say why: nothing else names the node the differ gave up on.
        if (IsDevelopment)
        {
            FullPageReply.Report(compared, _diffOps, renderCache.LastDiffForcedFullHtml, diffBytes, html.Span);
        }

        return false;
    }
}
