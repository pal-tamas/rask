using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop.Infrastructure;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Core.Routing;
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

    // Previous-frame buffer for the zero-copy send dedup: after SendAsync, _writeBuffer (base) and
    // this swap, so the just-sent bytes become the dedup baseline without a copy. Server-specific —
    // WASM ToArrays each frame for the JSImport boundary instead.
    private ArrayBufferWriter<byte>? _lastSentBuffer;

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

    public LiveSession(string id, Component view, IServiceScope scope)
        : base(view, scope.ServiceProvider)
    {
        Id = id;
        Scope = scope;
    }

    public bool SuppressEventsUntilReconnect { get; set; }

    public string Id { get; }
    public IServiceScope Scope { get; }
    public SemaphoreSlim Lock { get; } = new(1, 1);

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

    protected override Task RenderInScopeCoreAsync() => RenderAndSendAsync(null, false);

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
    internal void SeedInitialHtml(string html) => _lastAppliedHtml = html;

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
                _lastAppliedHtml = null;
            }

            // Render + decide diff-vs-full + write the frame — shared with the WASM host (LiveSessionBase).
            var html = RenderTreeToHtml(publishOnly, out var frameWriter);
            var download = ConsumeDownload();
            var jsInvokes = JsInvokes.Drain();

            // HTML-level dedup: when the rendered HTML matches the last sent (or the GET-seeded
            // baseline) and there's nothing out-of-band to flow, the payload would be byte-identical
            // to the last send — skip before building. Preserves JS-applied DOM state (hljs's `.hljs`
            // class) across noop publish-renders, and lets a fresh socket dedup against the GET HTML.
            if (jsInvokes is null
                && historyUrl is null && auth is null && download is null
                && _lastAppliedHtml is not null && string.Equals(html, _lastAppliedHtml, StringComparison.Ordinal))
            {
                return;
            }

            WritePayload(html, frameWriter, download, jsInvokes, historyUrl, replace,
                commitCache: true, auth, Id);

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
            _lastAppliedHtml = html;
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
