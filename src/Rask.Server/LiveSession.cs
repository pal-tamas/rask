using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop.Infrastructure;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Server.Authentication;
using Rask.Server.Diagnostics;
using Rask.Server.Files;
using Rask.Server.JSInterop;

namespace Rask.Server;

// Render/diff/payload pipeline + the IJSRuntime queue live in LiveSessionBase (Core), shared with
// the WASM host. LiveSession adds the WebSocket transport: the socket lifecycle, reconnect/force-
// resend, the dispatch lock, out-of-band sends, and the zero-copy double-buffered send dedup.
internal sealed class LiveSession : LiveSessionBase, IDisposable, IAsyncDisposable
{
    // Serialises individual RenderAndSendAsync calls within one handler dispatch. The dispatcher's
    // outer Lock pins single-handler-at-a-time; this inner gate keeps the mid-await render (on the
    // handler thread) from racing the HandlerSyncContext.RunWithRendersAsync renders (fired on
    // thread-pool workers from a user `await Task.Yield()` posting back through the captured
    // sync context). Two concurrent View.RenderAsLiveRoot walks on different threads otherwise
    // mutate the same Component state — _children, _stateDirty, _cachedRenderResult — and one
    // wins, dropping the other's payload, or both call _socket.SendAsync on the same WebSocket.
    private readonly SemaphoreSlim _renderLock = new(1, 1);

    // Set by AttachSocket on a reconnect to force the next render to bypass the HTML/buffer
    // dedup and re-emit even when the rendered bytes match the prior socket's last frame.
    // The dedup baselines (_lastSentBuffer/_lastSentHtml) are owned by the RenderAndSendAsync
    // critical section, which can run on a background thread (a StateHasChanged from an async
    // lifecycle continuation). Resetting them directly from AttachSocket would race that send,
    // so the reset is deferred into the render lock: RenderAndSendAsync reads-and-clears this
    // flag under _renderLock. Volatile so the cross-thread write is visible without the lock.
    private volatile bool _forceResend;

    // True after the first socket has ever attached to this session. Lets AttachSocket
    // distinguish the GET→hello first attach (where the browser's HTML is guaranteed to
    // match the session's state because the browser literally just rendered the GET
    // response) from a subsequent reconnect (where the browser may have missed the prior
    // socket's last frame due to a partial send, a tab background, or any other transport
    // gap we can't observe from this side). First-attach skips the redundant render;
    // reconnect always renders.
    private bool _hasAttachedBefore;

    // Set true whenever a render request lands with no live socket — async lifecycle
    // continuations from OnMountAsync / OnRenderedAsync that resolve during the HTTP-GET-
    // to-WS-hello handoff window, or while a session is between sockets across a
    // reconnect. AttachSocket reads it from the hello handler to decide whether a
    // catch-up render is actually needed: when nothing was dropped, the HTML the browser
    // already has from the GET response (or the prior socket) still matches the session
    // state and the hello-time render is redundant. Skipping the redundant render is
    // what aligns Server's initial-mount OnRendered count with WASM's.
    // Volatile: written lock-free by AttachSocket (WS-accept thread) and RequestRenderInternalAsync
    // (handler-dispatch thread), read lock-free by FlushPendingRenderAsync at hello time. Pairs with
    // the already-volatile _forceResend in the same reconnect handoff — both must be reliably visible
    // across threads or a dropped render goes unrecovered.
    private volatile bool _renderRequestedWhileDetached;

    // Volatile so AttachSocket's "publish _socket last" actually carries release semantics: the
    // volatile write makes the preceding _renderRequestedWhileDetached / _forceResend writes visible
    // before the new socket becomes observable, and a reader's acquiring read sees them in lockstep.
    // Without it the store-store order holds on x86 (TSO) but not on weaker models (ARM64), where a
    // concurrent render could observe the fresh socket yet miss the resend flags and drop the
    // reconnect catch-up frame.
    private volatile WebSocket? _socket;
    private CancellationToken _socketCt;

    // Set once disposal begins. Read by RequestRenderInternalAsync so a StateHasChanged fired
    // from a component's Unmount/Dispose callback can't re-enter the render path and deadlock on
    // the _renderLock that DisposeAsync/Dispose hold while tearing the tree down. Volatile because
    // disposal (store/host thread) and the render request (async lifecycle continuation on a
    // thread-pool worker) run on different threads. Mirrors the detached-socket early-return.
    private volatile bool _disposed;

    // How long one outbound frame may take before we give up on the client. Read from the per-host limits
    // when the host has them (a bare test store built straight from a ServiceCollection does not), so the
    // value is fixed for the session's lifetime rather than resolved on every send.
    private readonly TimeSpan _sendTimeout;

    // The host's meter, or null for a store built without one (tests, a bare harness). Held per session
    // so the render path records without resolving anything.
    private readonly RaskMetrics? _metrics;

    public LiveSession(
        string id, Component view, IServiceScope scope, LiveDiffMode diffMode, RaskMetrics? metrics = null)
        : base(view, scope.ServiceProvider, diffMode)
    {
        Id = id;
        Scope = scope;
        _sendTimeout = scope.ServiceProvider.GetService<RaskServerLimits>()?.SendTimeout ?? TimeSpan.Zero;
        _metrics = metrics;
    }

    public bool SuppressEventsUntilReconnect { get; set; }

    // A sign-in/out returnUrl parked by the handler-dispatch auth handoff and applied on the NEXT hello,
    // AFTER SessionUserProvider is re-seeded with the redeemed principal. Deferring the route change means
    // the destination page mounts fresh under the new identity (its OnMountAsync sees the new principal),
    // instead of mounting now under the pre-SignIn snapshot and never remounting on the reconnect
    // (children reconcile by (Type, position), not Key, so a same-position page instance is reused and
    // its OnMountAsync never re-runs — leaving data loaded for the old identity/tenant).
    public string? PendingAuthNavigation { get; set; }

    // What the client's current resume record was built from. -1 means it has none, so the first payload
    // always carries one. The URL is tracked alongside the version because a navigation moves the page
    // without touching the bag: version alone would leave the client holding a record that rebuilds the
    // route they were on two pages ago. Touched only from TakeResumeToken, which WritePayload calls under
    // the render lock.
    private int _lastResumeVersion = -1;
    private string? _lastResumeUrl;

    public string Id { get; }
    public IServiceScope Scope { get; }
    public SemaphoreSlim Lock { get; } = new(1, 1);

    private Task _lastHandlerTask = Task.CompletedTask;

    /// <summary>
    ///     Tail of the WS-message handler chain. Each inbound handler dispatch awaits
    ///     this task before running, then assigns its own continuation back here, so
    ///     handlers run strictly in WS-arrival order. <see cref="Task.CompletedTask" />
    ///     initially so the first handler runs immediately.
    ///     <para>
    ///         The WS receive loop is this property's sole <em>writer</em> and is single-threaded per
    ///         session, so writes never race each other. It gained a second <em>reader</em> on another
    ///         thread when the shutdown drain began awaiting the chain
    ///         (<see cref="RaskDrainService" />), hence the volatile accessors: reference assignment is
    ///         already atomic, but without the fence the drain thread can observe a stale tail on a weak
    ///         memory model and conclude a still-running handler had finished.
    ///     </para>
    /// </summary>
    internal Task LastHandlerTask
    {
        get => Volatile.Read(ref _lastHandlerTask);
        set => Volatile.Write(ref _lastHandlerTask, value);
    }

    /// <summary>Action dispatches queued on the chain but not yet complete. Read by the drain to settle.</summary>
    internal int PendingHandlers => Volatile.Read(ref _pendingHandlers);

    // Number of handler dispatches queued on the LastHandlerTask chain but not yet completed.
    // Each queued dispatch retains a cloned JsonElement, so an unbounded chain (a flood of input
    // arriving faster than handlers drain, or a single hung handler stalling the head) is a
    // memory-exhaustion vector. The receive loop reads this to apply backpressure. Interlocked
    // because the increment (receive loop) and decrement (dispatch continuation) can run on
    // different ThreadPool threads.
    private int _pendingHandlers;

    internal int IncrementPendingHandlers() => Interlocked.Increment(ref _pendingHandlers);

    internal void DecrementPendingHandlers() => Interlocked.Decrement(ref _pendingHandlers);

    // Aggregate bytes of the cloned payloads currently queued — the memory companion to the count
    // above, so the receive loop can bound the queue's footprint, not just its length.
    private long _pendingHandlerBytes;

    internal long AddPendingHandlerBytes(long bytes) => Interlocked.Add(ref _pendingHandlerBytes, bytes);

    internal void SubtractPendingHandlerBytes(long bytes) => Interlocked.Add(ref _pendingHandlerBytes, -bytes);

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        // Serialise teardown against any in-flight render. RenderAndSendAsync mutates the
        // component tree's child dictionaries under _renderLock (the swap+Clear in
        // Component.BuildRenderTree, then GetOrCreateChild inserts), and
        // DisposeComponentTreeAsync walks those same dictionaries. Without the lock a render
        // draining on a thread-pool thread at host shutdown races the walk and throws
        // "Collection was modified; enumeration operation may not execute" out of the dispose
        // enumeration — the very class of concurrent-tree-mutation bug _renderLock exists to
        // prevent (see its field comment). Hold it across the whole walk, then release before
        // disposing the semaphore itself.
        await _renderLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await ComponentLifecycle.DisposeComponentTreeAsync(View).ConfigureAwait(false);
            // Inside the lock, unlike the rest of teardown: these arrays go back to a shared pool, and a
            // render that raced this would then be writing into an array another session already owns.
            // Everything below fails loudly on a racing caller (a disposed semaphore or scope); this
            // would fail silently, which is the one outcome worth paying a few microseconds to avoid.
            ReleasePooledBuffers();
        }
        finally
        {
            _renderLock.Release();
        }

        ReleaseFileStores();
        Lock.Dispose();
        _renderLock.Dispose();
        Scope.Dispose();
    }

    public void Dispose()
    {
        _disposed = true;
        // See DisposeAsync: take the render lock so the synchronous tree walk can't race an
        // in-flight render mutating the same child dictionaries.
        _renderLock.Wait();
        try
        {
            ComponentLifecycle.DisposeComponentTree(View);
            // See DisposeAsync: returned under the lock so a racing render cannot write into an array
            // that now belongs to another session.
            ReleasePooledBuffers();
        }
        finally
        {
            _renderLock.Release();
        }

        ReleaseFileStores();
        Lock.Dispose();
        _renderLock.Dispose();
        Scope.Dispose();
    }

    protected override Task RenderInScopeCoreAsync() => RenderAndSendAsync(null, false);

    /// <summary>
    ///     Seals a resume record for this session when the page has moved since the last one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the only host that overrides it: a server session can outlive neither a restart nor
    ///         a redeploy, so the client has to be able to hand its page back to whatever process answers
    ///         next. Riding the render payload rather than its own frame means a handler that writes twenty
    ///         keys produces one record, and a render that changed nothing produces none.
    ///     </para>
    ///     <para>
    ///         A session whose bag has overflowed its budget is skipped — it has declared itself
    ///         unresumable, and a record it can never redeem is wire cost for nothing.
    ///     </para>
    /// </remarks>
    protected override string? TakeResumeToken()
    {
        if (_disposed || Scope.ServiceProvider.GetService<SessionResumeSupport>()?.Protector is not { } protector)
        {
            return null;
        }

        var state = Scope.ServiceProvider.GetRequiredService<PersistentState>();
        if (state.Overflowed)
        {
            return null;
        }

        var route = Scope.ServiceProvider.GetRequiredService<RouteState>();
        var url = QueryString.Build(route.Path, route.Query);
        if (state.Version == _lastResumeVersion && string.Equals(url, _lastResumeUrl, StringComparison.Ordinal))
        {
            return null;
        }

        // Mark issued before attempting the seal, not after: a protector that throws will throw again on
        // the next render too, and retrying it per frame would turn a broken key ring into a per-render
        // cost for a record that is never going to be produced.
        _lastResumeVersion = state.Version;
        _lastResumeUrl = url;

        try
        {
            return protector.Protect(
                url, Scope.ServiceProvider.GetRequiredService<SessionUserProvider>().Current, state.Entries);
        }
        catch (CryptographicException)
        {
            // No usable key ring (unwritable directory, revoked keys). Resume simply doesn't happen on this
            // host; it must not take the render down with it.
            return null;
        }
    }

    protected override async Task RequestRenderInternalAsync(bool publishOnly)
    {
        // _disposed short-circuits a StateHasChanged raised from an Unmount/Dispose callback during
        // teardown: the tree walk runs under _renderLock, so re-entering RenderAndSendAsync (which
        // also waits on _renderLock) would deadlock disposal against itself.
        if (_disposed || _socket is null || _socket.State != WebSocketState.Open)
        {
            _renderRequestedWhileDetached = true;
            return;
        }

        if (InHandlerScope)
        {
            // Defer to the dispatch's final RenderAndSendCoalescingAsync so a single
            // coalesced payload (carrying the captured historyUrl/replace/auth) reaches
            // the wire — multiple in-handler StateHasChanged calls would otherwise emit
            // intermediate payloads, double-morphing <head> and momentarily orphaning
            // the scoped-css <link> during navigation. publishOnly intentionally
            // dissolves into the dispatch's outer render flags, matching WASM
            // (Rask.Wasm/WasmLiveSession.cs:79-87).
            _pendingRenderInScope = true;
            return;
        }

        await Lock.WaitAsync(_socketCt).ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            await RenderAndSendAsync(null, false, publishOnly: publishOnly).ConfigureAwait(false);
        }
        finally
        {
            InHandlerScope = false;
            Lock.Release();
        }
    }

    /// <summary>
    ///     Hands this session's pooled arrays back to <see cref="ArrayPool{T}" />.
    /// </summary>
    /// <remarks>
    ///     The rendered-HTML buffer pair and the two frame writers behind the render cache are all pool
    ///     rentals held for the session's whole life. Dropping the references collects them but never
    ///     returns them, so every session teardown quietly cost the pool the arrays it had just warmed —
    ///     and the next session paid to allocate them again. On a large page these are the dominant
    ///     per-session term, so the cost scaled with page size.
    /// </remarks>
    private void ReleasePooledBuffers()
    {
        _htmlBuffers.Dispose();
        _renderCache?.Dispose();
    }

    private void ReleaseFileStores()
    {
        try
        {
            Services.GetService<SessionUploadStore>()?.ReleaseSession(Id);
            Services.GetService<SessionDownloadStore>()?.ReleaseSession(Id);
        }
        catch
        {
            // best-effort cleanup; do not let store errors mask disposal
        }
    }

    /// <summary>
    ///     Called by the HTTP-GET endpoint after the initial server-side render to seed
    ///     this session's dedup baseline with the HTML the browser is about to receive.
    ///     Without this, the first WS frame after hello has no baseline to compare
    ///     against and a no-op handler click would re-send the GET HTML verbatim;
    ///     seeding lets the HTML-level dedup in <see cref="RenderAndSendAsync" /> catch
    ///     identical-state renders the same way WASM's <c>InitialRenderAsync</c> does
    ///     via <c>_lastAppliedHtml</c>.
    /// </summary>
    internal void SeedInitialHtml(string html) => _htmlBuffers.SeedPrevious(html);

    /// <summary>
    ///     Render the initial root for the HTTP-GET response, seeding BOTH the dedup
    ///     baseline (<c>_lastSentHtml</c> via <see cref="SeedInitialHtml" />) and — when the
    ///     diff codec is on — the render-cache FRAME baseline. Capturing the GET render's
    ///     <c>RenderFrame</c> stream and <see cref="SessionRenderCache.Snapshot" />-ing it
    ///     means the FIRST interactive WS render diffs against the HTML the browser already
    ///     holds, instead of re-shipping the whole document. Without this the frame cache is
    ///     seeded only by the first full-HTML interactive send, so the first state change
    ///     after page load always ships the body in full.
    /// </summary>
    internal string RenderInitialRoot()
    {
        var html = RenderRootWave(publishOnly: false);
        CommitInitialRoot(html);
        return html;
    }

    /// <summary>
    ///     Render the shell, waiting up to <paramref name="budget" /> for the async lifecycle work
    ///     it starts to settle, so the HTML carries the page's data rather than its placeholders.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Runs in waves: render, wait for what that render started, render again. A wave is the
    ///         right unit because resolved data mounts new components, which start their own work —
    ///         a page whose list loads and whose rows then load is two waves, not one longer wait.
    ///     </para>
    ///     <para>
    ///         Waves after the first are <c>publishOnly</c>. That is not an optimisation: every
    ///         component has already rendered once, so a normal wave would re-fire
    ///         <c>OnRendered</c> on all of them, once per wave, each enqueuing another round of JS
    ///         interop.
    ///     </para>
    ///     <para>
    ///         The budget is one deadline for the whole response, not one per wave — otherwise ten
    ///         waves of five seconds is a fifty-second page. On expiry the caller is told through
    ///         <see cref="LastRenderTimedOut" />; the pending work is deliberately NOT cancelled,
    ///         because the page is about to be handed a live session that will finish the load.
    ///     </para>
    /// </remarks>
    internal async Task<string> RenderInitialRootAsync(TimeSpan budget)
    {
        if (budget <= TimeSpan.Zero)
        {
            return RenderInitialRoot();
        }

        using var quiescence = QuiescenceScope.Begin();
        var html = RenderRootWave(publishOnly: false);

        var deadline = DateTime.UtcNow + budget;
        var waves = 0;
        while (quiescence.TrySnapshotPending(out var batch))
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero || waves >= MaxQuiescenceWaves)
            {
                quiescence.MarkTimedOut();
                break;
            }

            try
            {
                await Task.WhenAll(batch).WaitAsync(remaining).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                quiescence.MarkTimedOut();
                break;
            }

            html = RenderRootWave(publishOnly: true);
            waves++;
        }

        LastRenderTimedOut = quiescence.TimedOut;
        CommitInitialRoot(html);
        return html;
    }

    /// <summary>
    ///     Whether the initial render was served before its async work settled. The host must keep a
    ///     live session for such a page: served as a static document it would sit on its placeholder
    ///     for ever, because nothing would be left running to replace it.
    /// </summary>
    internal bool LastRenderTimedOut { get; private set; }

    // A single render into the frame sink, WITHOUT promoting anything. Intermediate waves must not
    // touch either baseline: only the HTML actually served is what the browser will hold, so
    // committing a wave that is about to be superseded would leave the first live render diffing
    // against a document that never existed. Same discipline as the coalescing render loop's
    // deferred rotation.
    private string RenderRootWave(bool publishOnly)
    {
        if (DiffMode == LiveDiffMode.DisabledFull)
        {
            return View.RenderAsLiveRoot(Services, publishOnly);
        }

        _renderCache ??= new SessionRenderCache();
        var frameWriter = _renderCache.PrepareCurrentBuffer();
        using (FrameSinkScope.Push(frameWriter))
        {
            return View.RenderAsLiveRoot(Services, publishOnly);
        }
    }

    // Promote the served render to both baselines, exactly once.
    private void CommitInitialRoot(string html)
    {
        _renderCache?.Snapshot(); // promote GET frames to the diff baseline
        SeedInitialHtml(html);
    }

    // Bounds a pathological render whose every wave keeps starting new work. Mirrors the coalescing
    // loop's budget: past this the page is promoted to interactive and the live session finishes
    // the job, rather than the response growing without limit.
    private const int MaxQuiescenceWaves = 16;

    /// <summary>
    ///     Whether the last render fell back to the framework's error page rather than the app.
    /// </summary>
    /// <remarks>
    ///     The root boundary catches the exception, so a crashed page returns perfectly ordinary HTML
    ///     and the GET used to answer <c>200 OK</c> — telling every cache, crawler and uptime check that
    ///     the page was fine (#607).
    /// </remarks>
    internal bool LastRenderFaulted => View is RootErrorBoundary { RenderedFallback: true };

    public void AttachSocket(WebSocket socket, CancellationToken ct)
    {
        _socketCt = ct;
        SuppressEventsUntilReconnect = false;

        if (_hasAttachedBefore)
        {
            // Reconnect path — possibly a different browser tab/window — needs the current
            // HTML even when it byte-matches the prior socket's last frame, since the prior
            // send chain may have lost a frame. Force a catch-up and drop the dedup baselines
            // so the recovery render reliably emits. The baseline reset is deferred into the
            // render lock (see _forceResend) to avoid racing a background RenderAndSendAsync.
            _renderRequestedWhileDetached = true;
            _forceResend = true;
        }

        // Publish _socket last. _socket is volatile, so this write has release semantics: the
        // resend flags set above are guaranteed visible before the new socket is — a concurrent
        // background render either reads the old null/closed socket (early-returns, having recorded
        // the drop) or reads the new socket and sees the flags set. The ordering holds on weak
        // memory models too (see the _socket field note).
        _socket = socket;
        _hasAttachedBefore = true;
    }

    /// <summary>
    ///     Catch-up render at WS-hello time: emit a frame only if at least one render
    ///     request was dropped while the session had no live socket. Without a drop, the
    ///     HTML the browser already has (from the HTTP GET response, or the prior socket's
    ///     last frame on reconnect) still matches the session's state, so the hello-time
    ///     render is redundant — it would re-fire <c>OnRendered</c> on every alive
    ///     component for no observable change. Skipping it aligns Server's initial-mount
    ///     hook count with WASM's.
    /// </summary>
    internal Task FlushPendingRenderAsync()
    {
        // Render at hello whenever something needs to flow over WS that the GET response
        // couldn't carry: a dropped StateHasChanged from the handoff window, or a pending
        // IJSRuntime.InvokeAsync queued by an OnRendered / OnMountAsync sync path during
        // the GET render walk (the invoke sits in _pendingJsInvokes until the next outbound
        // frame). When neither is true, the browser's GET HTML still matches and there's
        // nothing for the WS to ship — skip the redundant render.
        var jsPending = JsInvokes.HasPending;

        if (!_renderRequestedWhileDetached && !jsPending)
        {
            return Task.CompletedTask;
        }

        // When the catch-up is only there to ship a queued js.InvokeVoidAsync (e.g. the
        // sibling-of-CodeSample case: CodeSample.OnRenderedAsync awaits IJSRuntime during
        // the GET render walk, queuing the invoke), use a publish-only render so already-
        // rendered components don't re-fire OnRendered. On WASM the same scenario routes
        // through OnRenderedAsync's RequestPublishRenderAsync after the JS call completes
        // — no extra OnRendered on siblings — so this keeps the initial-mount hook
        // sequence aligned across hosts. A genuine dropped StateHasChanged still triggers
        // a normal render (fires OnRendered) since that's the contract for state mutations.
        var publishOnly = !_renderRequestedWhileDetached && jsPending;
        _renderRequestedWhileDetached = false;
        return publishOnly ? RequestPublishRenderAsync() : RequestRenderAsync();
    }

    public void DetachSocket()
    {
        _socket = null;
        _socketCt = default;
    }

    /// <summary>
    ///     Out-of-band WS send for messages that aren't part of a render frame — currently
    ///     just <c>[JSInvokable]</c> .NET-call results pushed from
    ///     <see cref="JSInterop.RaskJSRuntime.EndInvokeDotNet" />. Single-writer-at-a-time via
    ///     the render lock so we don't interleave with an in-flight SendAsync.
    /// </summary>
    internal async Task SendOutOfBandAsync(ReadOnlyMemory<byte> payload)
    {
        if (_socket is null || _socket.State != WebSocketState.Open)
        {
            return;
        }

        await _renderLock.WaitAsync(_socketCt).ConfigureAwait(false);
        try
        {
            if (_socket is null || _socket.State != WebSocketState.Open)
            {
                return;
            }

            await SendGuardedAsync(payload).ConfigureAwait(false);
        }
        finally
        {
            _renderLock.Release();
        }
    }

    /// <summary>
    ///     Sends the WebSocket close frame for a graceful shutdown: status 1001
    ///     (<see cref="WebSocketCloseStatus.EndpointUnavailable" />, "going away") with a reason the
    ///     client can read. Best-effort — a socket already closing or faulted is not an error here.
    ///     <para>
    ///         <b>Why <c>CloseOutputAsync</c> and not <c>CloseAsync</c>.</b> This is called from the drain,
    ///         not from the receive loop, and the receive loop is parked in <c>ReceiveAsync</c> at the
    ///         time. <c>CloseAsync</c> sends the close frame <em>and then receives</em> until the peer
    ///         echoes, which would be a second concurrent receive and throws. <c>CloseOutputAsync</c> is
    ///         send-only: the parked <c>ReceiveAsync</c> then observes the browser's echo as an ordinary
    ///         close message and the loop unwinds through its own <c>finally</c>.
    ///     </para>
    ///     <para>
    ///         <b>Why not just cancel the socket token.</b> Cancelling a token a <c>ReceiveAsync</c> is
    ///         using <em>aborts</em> the WebSocket — no close frame, and the browser sees 1006. That is
    ///         precisely the behaviour this method exists to replace, so the cancellation path must stay
    ///         the deadline backstop and never the ordinary route.
    ///     </para>
    /// </summary>
    /// <param name="ct">
    ///     The drain's budget token. Deliberately not <c>_socketCt</c>: that one is cancelled by the
    ///     deadline, and using it here would make the close throw at exactly the moment it most needs to
    ///     complete.
    /// </param>
    internal async Task CloseForShutdownAsync(CancellationToken ct)
    {
        if (_socket is null || _socket.State != WebSocketState.Open)
        {
            return;
        }

        // Behind the render lock so the close frame cannot interleave a render's SendAsync — the same
        // discipline SendOutOfBandAsync uses, and the reason the shutdown announcement is guaranteed to
        // reach the wire before this close does.
        await _renderLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_socket is null || _socket.State != WebSocketState.Open)
            {
                return;
            }

            await _socket.CloseOutputAsync(
                WebSocketCloseStatus.EndpointUnavailable, "server-shutdown", ct).ConfigureAwait(false);
        }
        finally
        {
            _renderLock.Release();
        }
    }

    // Host transport for LiveSessionBase.TryEmitFrameAsync: write the frame's bytes to the WebSocket
    // (ReadOnlyMemory<byte>, zero-copy). RenderAndSendAsync guards _socket non-null/Open before the
    // shared send runs, and the render lock serialises teardown, so _socket is valid here.
    protected override ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame) => SendGuardedAsync(frame);

    /// <summary>
    ///     Writes one frame to the socket, giving up on a client that has stopped reading.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>SendAsync</c> completes when the frame reaches the transport, not when the client reads
    ///         it. A client that stops reading fills the send buffer and the send simply never returns —
    ///         and because every send here happens under <c>_renderLock</c>, which also guards teardown,
    ///         that one client would pin its session forever: no further renders, and a <c>Dispose</c> that
    ///         can never take the lock.
    ///     </para>
    ///     <para>
    ///         On timeout the socket is aborted, which is what unwinds the receive loop and releases the
    ///         lock. The <em>session</em> is left alone: it lives out its normal grace period, so a client
    ///         whose link stalled briefly reconnects to the page it already had.
    ///     </para>
    /// </remarks>
    private async ValueTask SendGuardedAsync(ReadOnlyMemory<byte> payload)
    {
        if (_sendTimeout <= TimeSpan.Zero)
        {
            await _socket!.SendAsync(payload, WebSocketMessageType.Text, true, _socketCt).ConfigureAwait(false);
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_socketCt);
        cts.CancelAfter(_sendTimeout);
        try
        {
            await _socket!.SendAsync(payload, WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!_socketCt.IsCancellationRequested)
        {
            // Ours, not the caller's: the send timed out rather than the session being torn down. Abort so
            // the receive loop unwinds, then report it as a transport failure — which is what it is, and
            // what every caller here already handles.
            RaskDiagnostics.Report(
                RaskLogLevel.Warning, "Rask.Live",
                $"Aborting a socket whose send did not complete within {_sendTimeout}. The client stopped "
                + "reading; its session is kept for the reconnect grace period.");
            try
            {
                _socket!.Abort();
            }
            catch
            {
                // Already torn down by the receive loop — nothing left to abort.
            }

            throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely, "Send timed out.");
        }
    }

    internal async Task RenderAndSendAsync(string? historyUrl, bool replace, AuthInstruction? auth = null,
        bool publishOnly = false)
    {
        if (_socket is null || _socket.State != WebSocketState.Open)
        {
            return;
        }

        await _renderLock.WaitAsync(_socketCt).ConfigureAwait(false);
        try
        {
            // Consume a reconnect's resend request inside the lock that owns the dedup
            // baselines, so AttachSocket never has to touch them from another thread. Clearing
            // both forces this render past the HTML dedup (below) and the buffer dedup (line
            // ~530), guaranteeing the catch-up frame reaches the freshly attached socket.
            if (_forceResend)
            {
                _forceResend = false;
                _lastSentBuffer = null;
                _htmlBuffers.Invalidate();
            }

            // Render + decide diff-vs-full + write the frame — shared with the WASM host (LiveSessionBase).
            var html = RenderTreeToHtml(publishOnly, out var frameWriter);
            var download = ConsumeDownload();
            var jsInvokes = JsInvokes.Drain();

            // HTML-level dedup: when the rendered HTML matches the last sent (or the GET-seeded
            // baseline) and there's nothing out-of-band to flow, the payload would be byte-identical
            // to the last send — skip before building. Preserves JS-applied DOM state (hljs's `.hljs`
            // class) across noop publish-renders, and lets a fresh socket dedup against the GET HTML.
            // HasPendingDevError defeats this deliberately: a handler that threw and changed nothing
            // renders byte-identical HTML, so without it the overlay would never reach the browser in
            // exactly the simplest case — a click whose only effect was the exception.
            if (jsInvokes is null
                && historyUrl is null && auth is null && download is null
                && !HasPendingDevError
                && _htmlBuffers.CurrentEqualsPrevious())
            {
                return;
            }

            var renderStarted = Stopwatch.GetTimestamp();

            // Read before WritePayload consumes it — the byte-level dedup below needs to know too.
            var devErrorPending = HasPendingDevError;

            WritePayload(html, frameWriter, download, jsInvokes, historyUrl, replace,
                commitCache: true, auth, Id);

            // Emit via the shared double-buffered send (dedup + swap in LiveSessionBase). Force the
            // send when something out-of-band (navigation, auth, download) must flow even if the
            // rendered bytes are byte-identical to the previous frame — otherwise the dedup skips it.
            // Read BEFORE the emit: TryEmitFrameAsync swaps _writeBuffer with the previous-frame buffer
            // for the zero-copy dedup, so afterwards this field is the OTHER buffer and its count is the
            // reset one. Measuring it there silently records zero for every frame.
            var payloadBytes = _writeBuffer.WrittenCount;

            var force = historyUrl is not null || auth is not null || download is not null
                        || devErrorPending;
            bool sent;
            try
            {
                sent = await TryEmitFrameAsync(force).ConfigureAwait(false);
            }
            catch (Exception ex) when (jsInvokes is not null)
            {
                // The queue was already drained at line ~225, so any taskIds in this batch
                // would otherwise hang their awaiting Task<T> forever — the JS side never
                // received the request, so no jsResult is coming back. Fail them locally
                // with the send error before letting the exception propagate so callers see
                // a meaningful JSException instead of an infinite await.
                FailPendingJsInvokes(jsInvokes, ex);
                throw;
            }

            if (sent)
            {
                _htmlBuffers.Commit();

                // Only when a frame actually went out. Recording a deduped render — one whose bytes
                // matched the last frame and was suppressed — would put a zero-byte sample in the payload
                // histogram and count work the client never saw, which is precisely the case these two
                // signals exist to distinguish from a real one.
                _metrics?.RecordRenderDuration(Stopwatch.GetElapsedTime(renderStarted).TotalMilliseconds);
                _metrics?.RecordPayloadBytes(payloadBytes);
            }
        }
        finally
        {
            _renderLock.Release();
        }
    }

    /// <summary>
    ///     Dispatch-tail render that coalesces in-handler StateHasChanged calls into a
    ///     single outbound payload. The first send emits the captured navigation/auth
    ///     state; if an in-handler StateHasChanged set <c>_pendingRenderInScope</c>
    ///     (e.g. <c>RouteState.Changed</c> subscribers on a layout above the Router),
    ///     rebuild up to twice — re-threading <paramref name="historyUrl" />,
    ///     <paramref
    ///         name="replace" />
    ///     , and <paramref name="auth" /> so the actually-sent payload
    ///     still carries them. Dropping any of those on a rebuild silently swallows
    ///     handler-initiated navigation, the c11b61d invariant ported from
    ///     <c>WasmLiveSession.BuildPayloadCoalescingRerendersAsync</c>. The
    ///     <paramref name="historyUrl" /> here is a local captured before the loop, not
    ///     a fresh <c>Navigator.TryConsumeHistory</c> call, so re-passing it on every
    ///     iteration does not double-push history on the client — only one frame ever
    ///     reaches the wire because <see cref="RenderAndSendAsync" />'s byte-dedup
    ///     suppresses spurious identical rebuilds.
    /// </summary>
    internal async Task RenderAndSendCoalescingAsync(
        string? historyUrl, bool replace, AuthInstruction? auth = null)
    {
        _pendingRenderInScope = false;
        await RenderAndSendAsync(historyUrl, replace, auth).ConfigureAwait(false);
        var budget = 2;
        while (_pendingRenderInScope && budget-- > 0)
        {
            _pendingRenderInScope = false;
            await RenderAndSendAsync(historyUrl, replace, auth).ConfigureAwait(false);
        }

        // Budget exhaustion was silent before: a third in-dispatch StateHasChanged
        // never flushed, the next event's payload picked up the trailing state, and
        // diagnosing "my UI is one render behind" required reading the source. A
        // warning gives the user a single grep target.
        if (_pendingRenderInScope)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Warning, "Rask.Live",
                $"[Rask.LiveSession] Coalesce-loop budget exhausted for session {Id}; " +
                "a third in-dispatch render was queued and dropped. Inspect any handlers " +
                "that re-trigger StateHasChanged in OnRenderedAsync / dispose callbacks " +
                "during this dispatch.");
        }
    }

    // Synthesises a [taskId, false, error] reply for every drained invoke and feeds it
    // back into DotNetDispatcher.EndInvokeJS — same shape RaskEndpointExtensions.HandleJsResult
    // uses for an honest browser-supplied jsResult. Used when the WS send fails after the
    // queue is already cleared. Best-effort: a missing runtime / dispatcher throw means we
    // log and move on; the original send exception is the meaningful one for the caller, so
    // a fault in here must never mask it (the catch re-establishes that contract — without it
    // a throwing Fail would propagate from RenderAndSendAsync in place of the send error, and
    // the awaiting Task<T> the Fail was meant to complete would still hang).
    private void FailPendingJsInvokes(PendingJsInvoke[] invokes, Exception cause)
    {
        try
        {
            if (Services.GetService<RaskJSRuntime>() is { } runtime)
            {
                LiveJsInvokeQueue.Fail(runtime, invokes,
                    string.IsNullOrEmpty(cause.Message)
                        ? "Rask: WebSocket send failed before JS invoke could be dispatched"
                        : cause.Message);
            }
        }
        catch (Exception failEx)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Error, "Rask.Live",
                $"[Rask.LiveSession] Failed to fault {invokes.Length} pending JS invoke(s) for " +
                $"session {Id} after a send error", failEx);
        }
    }
}
