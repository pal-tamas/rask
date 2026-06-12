using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop.Infrastructure;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Server.Files;
using Rask.Server.JSInterop;

namespace Rask.Server;

internal sealed class LiveSession : IDisposable, IAsyncDisposable, IRenderHandle
{
    // IJSRuntime queue. Calls land here via RaskJSRuntime.BeginInvokeJS and get drained
    // into the next outbound payload by RenderAndSendAsync. A plain List under lock —
    // contention is bounded by the session's outer Lock semaphore (one handler at a time),
    // so writes only race with the drain at flush time.
    private readonly List<PendingJsInvoke> _pendingJsInvokes = new();

    // Serialises individual RenderAndSendAsync calls within one handler dispatch. The dispatcher's
    // outer Lock pins single-handler-at-a-time; this inner gate keeps the mid-await render (on the
    // handler thread) from racing the HandlerSyncContext.RunWithRendersAsync renders (fired on
    // thread-pool workers from a user `await Task.Yield()` posting back through the captured
    // sync context). Two concurrent View.RenderAsLiveRoot walks on different threads otherwise
    // mutate the same Component state — _children, _stateDirty, _cachedRenderResult — and one
    // wins, dropping the other's payload, or both call _socket.SendAsync on the same WebSocket.
    private readonly SemaphoreSlim _renderLock = new(1, 1);

    private List<EditOp>? _diffOps;

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

    private ArrayBufferWriter<byte>? _lastSentBuffer;

    // Last rendered HTML (the `html` string the framework produced last time we
    // sent a frame). Used to skip noop publish-renders that would otherwise
    // re-morph identical HTML and clobber JS-applied DOM state (e.g. the
    // `.hljs` class hljs added to <code> elements after the previous
    // OnRenderedAsync invoke completed). Set after a successful send.
    private string? _lastSentHtml;

    // Set by RequestRenderInternalAsync when StateHasChanged fires while a handler
    // dispatch is mid-flight (InHandlerScope=true). RenderAndSendCoalescingAsync
    // reads and clears it to rebuild the payload before releasing the dispatch lock —
    // captures state mutated by RouteState.Changed subscribers / dispose callbacks
    // that fire after the first ToHtml walk but before the dispatch returns. Without
    // this, every in-handler StateHasChanged emits a separate intermediate payload,
    // double-morphing <head> on every nav and momentarily orphaning the scoped-css
    // <link> (visible as a one-frame flash of the sidebar's .nav-item-btn styling).
    // Mirrors WasmLiveSession._pendingRenderInScope (Rask.Wasm/WasmLiveSession.cs:41).
    private bool _pendingRenderInScope;

    // Diff-codec state. Populated only when LiveOptions.DiffMode != DisabledFull, so
    // the default path pays nothing for these. Both fields are lazy because the
    // common production path today still ships full HTML.
    private SessionRenderCache? _renderCache;

    // Set true whenever a render request lands with no live socket — async lifecycle
    // continuations from OnMountAsync / OnRenderedAsync that resolve during the HTTP-GET-
    // to-WS-hello handoff window, or while a session is between sockets across a
    // reconnect. AttachSocket reads it from the hello handler to decide whether a
    // catch-up render is actually needed: when nothing was dropped, the HTML the browser
    // already has from the GET response (or the prior socket) still matches the session
    // state and the hello-time render is redundant. Skipping the redundant render is
    // what aligns Server's initial-mount OnRendered count with WASM's.
    private bool _renderRequestedWhileDetached;
    private WebSocket? _socket;
    private CancellationToken _socketCt;

    // Set once disposal begins. Read by RequestRenderInternalAsync so a StateHasChanged fired
    // from a component's Unmount/Dispose callback can't re-enter the render path and deadlock on
    // the _renderLock that DisposeAsync/Dispose hold while tearing the tree down. Volatile because
    // disposal (store/host thread) and the render request (async lifecycle continuation on a
    // thread-pool worker) run on different threads. Mirrors the detached-socket early-return.
    private volatile bool _disposed;

    // Two-buffer swap: `_writeBuffer` receives the next frame, `_lastSentBuffer` holds the
    // previous send (dedup compare target). After SendAsync the references swap so the just-
    // sent buffer becomes the dedup baseline without any byte[] copy. Both writers persist
    // across the session's lifetime; ResetWrittenCount keeps the underlying rented array hot.
    private ArrayBufferWriter<byte> _writeBuffer = new(4096);

    public LiveSession(string id, Component view, IServiceScope scope)
    {
        Id = id;
        View = view;
        Scope = scope;
        view.RenderHandle = this;
        // RootErrorBoundary wraps the user's App; forward the handle to the inner so its
        // StateHasChanged() still reaches the session even before the first GetOrCreate
        // (which would otherwise be where the handle gets lazily attached).
        if (view is RootErrorBoundary root)
        {
            root.Inner.RenderHandle = this;
        }
    }

    public bool SuppressEventsUntilReconnect { get; set; }

    public string Id { get; }
    public Component View { get; }
    public IServiceScope Scope { get; }
    public IServiceProvider Services => Scope.ServiceProvider;
    public SemaphoreSlim Lock { get; } = new(1, 1);

    // Plain instance bool, NOT AsyncLocal: the dispatch lock is owned by this session as a whole,
    // not by any one async chain. AsyncLocal would flow into Timer/Task captures created during a
    // dispatch — those captured ExecutionContexts would later report InHandlerScope=true forever,
    // making background StateHasChanged calls (e.g. EditContext's sticky-dismissal Timer or a
    // user component's Timer) hit the in-scope branch — which under the coalescing model just
    // sets _pendingRenderInScope and returns, leaving the render to no one. WASM uses the same
    // plain-bool approach for the same reason (Rask.Wasm/WasmLiveSession.cs:30-33).
    public bool InHandlerScope { get; set; }

    /// <summary>
    ///     Tail of the WS-message handler chain. Each inbound handler dispatch awaits
    ///     this task before running, then assigns its own continuation back here, so
    ///     handlers run strictly in WS-arrival order. The WS receive loop is single-
    ///     threaded for this session, so reads / writes of this property don't race —
    ///     no synchronisation needed. <see cref="Task.CompletedTask" /> initially so
    ///     the first handler runs immediately.
    /// </summary>
    internal Task LastHandlerTask { get; set; } = Task.CompletedTask;

    // Number of handler dispatches queued on the LastHandlerTask chain but not yet completed.
    // Each queued dispatch retains a cloned JsonElement, so an unbounded chain (a flood of input
    // arriving faster than handlers drain, or a single hung handler stalling the head) is a
    // memory-exhaustion vector. The receive loop reads this to apply backpressure. Interlocked
    // because the increment (receive loop) and decrement (dispatch continuation) can run on
    // different ThreadPool threads.
    private int _pendingHandlers;

    internal int IncrementPendingHandlers() => Interlocked.Increment(ref _pendingHandlers);

    internal void DecrementPendingHandlers() => Interlocked.Decrement(ref _pendingHandlers);

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

    public Task RequestRenderAsync() => RequestRenderInternalAsync(false);

    public Task RequestPublishRenderAsync() => RequestRenderInternalAsync(true);

    Task IRenderHandle.RenderInScopeAsync() => RenderAndSendAsync(null, false);

    private async Task RequestRenderInternalAsync(bool publishOnly)
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
    internal void SeedInitialHtml(string html) => _lastSentHtml = html;

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
        string html;
        if (LiveOptions.DiffMode != LiveDiffMode.DisabledFull)
        {
            _renderCache ??= new SessionRenderCache();
            var frameWriter = _renderCache.PrepareCurrentBuffer();
            using (FrameSinkScope.Push(frameWriter))
            {
                html = View.RenderAsLiveRoot(Services);
            }

            _renderCache.Snapshot(); // promote GET frames to the diff baseline
        }
        else
        {
            html = View.RenderAsLiveRoot(Services);
        }

        SeedInitialHtml(html);
        return html;
    }

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

        // Publish _socket last: a concurrent background render early-returns while it reads
        // null/closed, so the new socket only becomes visible after the resend flag above.
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
        bool jsPending;
        lock (_pendingJsInvokes)
        {
            jsPending = _pendingJsInvokes.Count > 0;
        }

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
    ///     Queue a global-JS interop call (from <see cref="JSInterop.RaskJSRuntime" />) to be
    ///     emitted on the next outbound frame. Thread-safe — calls can arrive from awaited
    ///     continuations on thread-pool workers.
    /// </summary>
    internal void EnqueueJsInvoke(PendingJsInvoke invoke)
    {
        lock (_pendingJsInvokes)
        {
            _pendingJsInvokes.Add(invoke);
        }
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

            await _socket.SendAsync(payload, WebSocketMessageType.Text, true, _socketCt).ConfigureAwait(false);
        }
        finally
        {
            _renderLock.Release();
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
                _lastSentHtml = null;
            }

            // Diff-codec path: capture the parallel RenderFrame[] stream during render so
            // we can produce a minimal edit-op payload instead of re-shipping the whole
            // body. Default (LiveDiffMode.DisabledFull) bypasses this entirely — the
            // ambient FrameSinkScope is null on entry and HtmlSerializer's frame emit
            // is a single null check per branch. Only the opted-in path pays for the
            // FrameWriter, the diff walk, and the SessionRenderCache buffer rotation.
            var diffMode = LiveOptions.DiffMode;
            FrameWriter? frameWriter = null;
            FrameSinkScope.Popper framePopper = default;
            if (diffMode != LiveDiffMode.DisabledFull)
            {
                _renderCache ??= new SessionRenderCache();
                frameWriter = _renderCache.PrepareCurrentBuffer();
                framePopper = FrameSinkScope.Push(frameWriter);
            }

            string html;
            try
            {
                html = View.RenderAsLiveRoot(Services, publishOnly);
            }
            finally
            {
                if (frameWriter is not null)
                {
                    framePopper.Dispose();
                }
            }

            PendingDownload? download = null;
            if (Services.GetService<IDownloadSink>() is { } sink && sink.TryConsume(out var pd))
            {
                download = pd;
            }

            // BuildPayloadUtf8WithRoot encodes the rendered HTML to UTF-8 once, splices
            // data-rask-root onto <body>, and emits the WHOLE document (Doctype + Html +
            // Head + Body). Sending the full document — same shape WASM uses — lets the
            // client morph document.documentElement and pick up <head> changes
            // (<title>, per-page Head asset contributions, scoped CSS/JS hash bumps)
            // across in-app navigations. Body-only payloads froze <head> at whatever
            // the initial HTTP GET produced, so a per-page Title declared via
            // Component.Head never made it to the browser tab on SPA-style navigation.
            // The added head bytes (~2-3 KB) compress away under permessage-deflate.
            PendingJsInvoke[]? jsInvokes = null;
            lock (_pendingJsInvokes)
            {
                if (_pendingJsInvokes.Count > 0)
                {
                    jsInvokes = _pendingJsInvokes.ToArray();
                    _pendingJsInvokes.Clear();
                }
            }

            // HTML-level dedup: when the rendered HTML matches the last sent (or the
            // HTTP-GET-time seeded baseline) and there's nothing out-of-band to flow,
            // the resulting payload would be byte-identical to what we sent last —
            // skip without even building the payload. Originally a publish-only-render
            // guard (preserves JS-applied DOM state like hljs's `.hljs` class across
            // noop publish-renders); generalised so a fresh socket can also dedup
            // against the GET-time HTML the browser already has, matching WASM's
            // InitialRenderAsync dedup-baseline behaviour.
            if (jsInvokes is null
                && historyUrl is null && auth is null && download is null
                && _lastSentHtml is not null && string.Equals(html, _lastSentHtml, StringComparison.Ordinal))
            {
                return;
            }

            _writeBuffer.ResetWrittenCount();

            // Decide payload shape. Default (DisabledFull) and any case where we can't
            // confidently ship a diff (first render, out-of-band side effects, ops we
            // can't yet apply) routes to the full-HTML path. The diff path fires
            // when LiveOptions.DiffMode is Auto/Forced AND we have a clean diff — under
            // Auto we ship it whenever it's not larger than re-sending the body.
            var usedDiff = false;
            var diffPathEntered = false;
            // Out-of-band side effects (auth, download) and structural ops
            // (InsertSubtree/RemoveSubtree) still need the full-HTML payload. Why
            // structural ops: e2e validation (83 of 430 tests failed when the structural
            // gate was lifted) showed naive InsertSubtree/RemoveSubtree on top of the
            // morph baseline produces DOM states the morph library wouldn't have —
            // preserved focused inputs, event-listener identity on swapped elements,
            // dialog open/close state. Until applyDiff reaches morph-quality book-keeping,
            // full HTML wins for structural changes (the DiffOpsAreClientSupported gate
            // below rejects untrusted positional structural ops independently of history).
            //
            // Head changes (a per-route <title>, a scoped-asset <link> for a newly-mounted
            // page type, a reactive title) ride the diff too. The diff frame stream never
            // carries <head> content — user Head contributions are collected and spliced
            // post-render (HeadAssetRegistry), so a head change produces zero ops. When the
            // head region changed vs the last sent document we attach the new <head> element
            // (ExtractHead) to the diff payload; the client morphs it into document.head
            // alongside applying the body ops. This covers navigation AND non-navigation
            // reactive title updates — the latter previously shipped a body diff that froze
            // the head. Genuine page swaps that restructure the body still fall back to full
            // HTML via the DiffOpsAreClientSupported gate below (head fragment never sent).
            //
            // jsInvokes do NOT gate the diff: fire-and-forget IJSRuntime calls (e.g. a
            // scoped-JS OnRenderedAsync hook firing on every render) ride the diff
            // payload via BuildPayloadUtf8Diff, matching WASM — which dispatches such
            // calls out-of-band and never forced full HTML. When we DO fall back to
            // full HTML (first render, oversized diff, structural ops) the client morphs to
            // match the server's frame snapshot, so the next render diffs cleanly against it.
            if (frameWriter is not null && _renderCache is not null
                                        && auth is null && download is null)
            {
                _diffOps ??= new List<EditOp>();
                diffPathEntered = true;
                var headChanged = _lastSentHtml is not null && !LiveDiffGate.HeadUnchanged(html, _lastSentHtml);
                // Ship the diff when it carries DOM ops, OR when it carries no ops but a
                // navigation or a head change needs to flow — a query-only nav (or any nav
                // that doesn't alter the body) produces zero ops yet must still pushState the
                // URL; a head-only change must still ship the head fragment. Zero ops + no
                // history + unchanged head means nothing to send (the html-dedup above
                // already returned for that case).
                if (_renderCache.TryComputeDiff(_diffOps, html)
                    && (_diffOps.Count > 0 || historyUrl is not null || headChanged)
                    && LiveDiffGate.DiffOpsAreClientSupported(_diffOps))
                {
                    var headHtml = headChanged ? LiveDiffGate.ExtractHead(html) : null;
                    LivePayload.BuildPayloadUtf8Diff(_writeBuffer, _diffOps, historyUrl, replace, jsInvokes, headHtml);
                    var diffBytes = _writeBuffer.WrittenCount;

                    // Ship the diff whenever it isn't larger than re-sending the body, or
                    // unconditionally under Forced. The only case we fall back to full
                    // HTML on size is the pathological one where nearly every node changed
                    // (typically a tiny page) and the op-list framing (paths + values +
                    // JSON) exceeds the body itself — then the diff would cost more bytes
                    // than the baseline. Every genuine in-place state change (counter,
                    // text, attribute, keyed list edit) is far smaller than the body and
                    // takes the diff path regardless of page size.
                    if (diffMode == LiveDiffMode.Forced || diffBytes < html.Length)
                    {
                        usedDiff = true;
                    }
                    else
                    {
                        _writeBuffer.ResetWrittenCount();
                    }
                }
            }

            if (!usedDiff)
            {
                LivePayload.BuildPayloadUtf8WithRoot(_writeBuffer, html, Id, historyUrl, replace,
                    auth, download, jsInvokes);
                // Cache stays in lockstep with the client even when we ship full HTML —
                // promote current → previous so the NEXT diff's baseline matches what
                // the client most recently received. Only when we did NOT enter the
                // diff branch: TryComputeDiff already rotates buffers internally, so a
                // second Snapshot here would double-rotate and strand _previous=null.
                if (!diffPathEntered)
                {
                    _renderCache?.Snapshot();
                }
            }

            // Skip the frame when the payload is byte-identical to the previous one AND nothing
            // out-of-band (navigation, auth instruction) needs to flow. Catches handler invocations
            // that ended up not modifying tracked state. SequenceEqual is SIMD-accelerated and
            // Utf8JsonWriter is deterministic, so byte equality is equivalent to the previous
            // string-Ordinal compare.
            if (historyUrl is null && auth is null && download is null
                && _lastSentBuffer is not null
                && _writeBuffer.WrittenSpan.SequenceEqual(_lastSentBuffer.WrittenSpan))
            {
                return;
            }

            try
            {
                await _socket.SendAsync(_writeBuffer.WrittenMemory, WebSocketMessageType.Text, true, _socketCt)
                    .ConfigureAwait(false);
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

            // Swap: the buffer we just sent becomes next frame's dedup baseline; the previous
            // baseline (or a fresh writer on first send) is reused as the next write target.
            (_lastSentBuffer, _writeBuffer) = (_writeBuffer, _lastSentBuffer ?? new ArrayBufferWriter<byte>(4096));
            _lastSentHtml = html;
        }
        finally
        {
            _renderLock.Release();
        }
    }

    /// <summary>
    ///     Dispatch-tail render that coalesces in-handler StateHasChanged calls into a
    ///     single outbound payload. The first send emits the captured navigation/auth
    ///     state; if an in-handler StateHasChanged set <see cref="_pendingRenderInScope" />
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
            Console.Error.WriteLine(
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
    // log and move on; the original send exception is the meaningful one for the caller.
    private void FailPendingJsInvokes(PendingJsInvoke[] invokes, Exception cause)
    {
        var runtime = Services.GetService<RaskJSRuntime>();
        if (runtime is null)
        {
            return;
        }

        var message = cause.Message;
        if (string.IsNullOrEmpty(message))
        {
            message = "Rask: WebSocket send failed before JS invoke could be dispatched";
        }

        foreach (var invoke in invokes)
        {
            try
            {
                using var stream = new MemoryStream(128);
                using (var w = new Utf8JsonWriter(stream))
                {
                    w.WriteStartArray();
                    w.WriteNumberValue(invoke.TaskId);
                    w.WriteBooleanValue(false);
                    w.WriteStringValue(message);
                    w.WriteEndArray();
                }

                DotNetDispatcher.EndInvokeJS(runtime, Encoding.UTF8.GetString(stream.ToArray()));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"Rask: failed to surface JS invoke fault for taskId={invoke.TaskId}: {ex}");
            }
        }
    }
}
