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
    // Serialises individual RenderAndSendAsync calls within one handler dispatch. The dispatcher's
    // outer Lock pins single-handler-at-a-time; this inner gate keeps the mid-await render (on the
    // handler thread) from racing the HandlerSyncContext.RunWithRendersAsync renders (fired on
    // thread-pool workers from a user `await Task.Yield()` posting back through the captured
    // sync context). Two concurrent View.RenderAsLiveRoot walks on different threads otherwise
    // mutate the same Component state — _children, _stateDirty, _cachedRenderResult — and one
    // wins, dropping the other's payload, or both call _socket.SendAsync on the same WebSocket.
    private readonly SemaphoreSlim _renderLock = new(1, 1);

    // IJSRuntime queue. Calls land here via RaskJSRuntime.BeginInvokeJS and get drained
    // into the next outbound payload by RenderAndSendAsync. A plain List under lock —
    // contention is bounded by the session's outer Lock semaphore (one handler at a time),
    // so writes only race with the drain at flush time.
    private readonly List<PendingJsInvoke> _pendingJsInvokes = new();
    private ArrayBufferWriter<byte>? _lastSentBuffer;
    // Last rendered HTML (the `html` string the framework produced last time we
    // sent a frame). Used to skip noop publish-renders that would otherwise
    // re-morph identical HTML and clobber JS-applied DOM state (e.g. the
    // `.hljs` class hljs added to <code> elements after the previous
    // OnRenderedAsync invoke completed). Set after a successful send.
    private string? _lastSentHtml;
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

    // Two-buffer swap: `_writeBuffer` receives the next frame, `_lastSentBuffer` holds the
    // previous send (dedup compare target). After SendAsync the references swap so the just-
    // sent buffer becomes the dedup baseline without any byte[] copy. Both writers persist
    // across the session's lifetime; ResetWrittenCount keeps the underlying rented array hot.
    private ArrayBufferWriter<byte> _writeBuffer = new(4096);

    // Diff-codec state. Populated only when LiveOptions.DiffMode != DisabledFull, so
    // the default path pays nothing for these. Both fields are lazy because the
    // common production path today still ships full HTML.
    private SessionRenderCache? _renderCache;
    private List<EditOp>? _diffOps;

    // Set when the previous render shipped full HTML with jsInvokes pending. The
    // next render — the one that runs after the JS-side returns its jsResult and
    // the awaiting handler resumes — must also ship full HTML, NOT a diff. Why:
    // the await-resume continuation can land outside the dispatch's normal serial
    // ordering (continuations on LifecycleSyncContext), and the cache rotation
    // assumptions held by the diff path stop applying. Sticking with full HTML
    // for one more render forces a morph that re-bases the client DOM, then the
    // diff path can resume safely on the render after that.
    private bool _forceFullHtmlNextRender;

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

    public async ValueTask DisposeAsync()
    {
        await ComponentLifecycle.DisposeComponentTreeAsync(View).ConfigureAwait(false);
        ReleaseFileStores();
        Lock.Dispose();
        _renderLock.Dispose();
        Scope.Dispose();
    }

    public void Dispose()
    {
        ComponentLifecycle.DisposeComponentTree(View);
        ReleaseFileStores();
        Lock.Dispose();
        _renderLock.Dispose();
        Scope.Dispose();
    }

    public Task RequestRenderAsync() => RequestRenderInternalAsync(publishOnly: false);

    public Task RequestPublishRenderAsync() => RequestRenderInternalAsync(publishOnly: true);

    private async Task RequestRenderInternalAsync(bool publishOnly)
    {
        if (_socket is null || _socket.State != WebSocketState.Open)
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

    Task IRenderHandle.RenderInScopeAsync() => RenderAndSendAsync(null, false);

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

    // True after the first socket has ever attached to this session. Lets AttachSocket
    // distinguish the GET→hello first attach (where the browser's HTML is guaranteed to
    // match the session's state because the browser literally just rendered the GET
    // response) from a subsequent reconnect (where the browser may have missed the prior
    // socket's last frame due to a partial send, a tab background, or any other transport
    // gap we can't observe from this side). First-attach skips the redundant render;
    // reconnect always renders.
    private bool _hasAttachedBefore;

    public void AttachSocket(WebSocket socket, CancellationToken ct)
    {
        _socket = socket;
        _socketCt = ct;
        SuppressEventsUntilReconnect = false;
        // A reconnect — possibly a different browser tab/window — needs the current HTML
        // even when it byte-matches the prior socket's last frame. Reset the dedup baseline
        // so the recovery render reliably emits.
        _lastSentBuffer = null;

        if (_hasAttachedBefore)
        {
            // Reconnect path — force a catch-up regardless of whether anything was
            // observably dropped, since the prior send chain may have lost a frame.
            // Also drop the HTML dedup baseline so the catch-up render reliably
            // emits even when the browser's stale HTML matches the session's current
            // render verbatim (the new socket's browser tab needs the bytes either way).
            _renderRequestedWhileDetached = true;
            _lastSentHtml = null;
        }

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
            // Out-of-band side effects (auth, download, jsInvokes), history changes
            // (navigation), and structural ops (InsertSubtree/RemoveSubtree) still
            // need the full-HTML payload. Why structural ops: e2e validation (83 of
            // 430 tests failed when the structural gate was lifted) showed naive
            // InsertSubtree/RemoveSubtree on top of the morph baseline produces DOM
            // states the morph library wouldn't have — preserved focused inputs,
            // event-listener identity on swapped elements, dialog open/close state.
            // Until applyDiff reaches morph-quality book-keeping, full HTML wins for
            // structural changes. _forceFullHtmlNextRender keeps the render
            // immediately after a jsInvokes payload on the full-HTML path so the
            // await-resume continuation re-bases the client DOM via morph.
            if (frameWriter is not null && _renderCache is not null
                && auth is null && download is null && jsInvokes is null
                && historyUrl is null
                && !_forceFullHtmlNextRender)
            {
                _diffOps ??= new List<EditOp>();
                diffPathEntered = true;
                if (_renderCache.TryComputeDiff(_diffOps, html)
                    && _diffOps.Count > 0
                    && DiffOpsAreClientSupported(_diffOps))
                {
                    LivePayload.BuildPayloadUtf8Diff(_writeBuffer, _diffOps, historyUrl, replace);
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

            // Update the one-render-grace flag. If we shipped with jsInvokes pending,
            // the next render (the await-resume continuation) must also be full HTML.
            // Otherwise, clear it so subsequent renders return to the diff path.
            _forceFullHtmlNextRender = jsInvokes is not null;

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
    ///     rebuild up to twice — re-threading <paramref name="historyUrl" />, <paramref
    ///     name="replace" />, and <paramref name="auth" /> so the actually-sent payload
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
    // Structural ops (Insert/Remove/Move) route to the full-HTML morph path UNLESS they
    // were produced by FrameDiffer's keyed-matching branch (EditOp.Trusted=true). Keyed
    // matching identifies survivors by data-rask-key, so a structural op only fires when
    // a node truly entered or left the keyed set — focus/listener/IDL state on surviving
    // nodes stays intact (Moves don't materialise new DOM, they re-parent the same node).
    // Positional structural ops can replace mid-list elements that the morph would have
    // preserved, which broke 83/430 e2e tests in an earlier iteration that ungated them
    // unconditionally — that's why we still route those through the full-HTML path.
    private static bool DiffOpsAreClientSupported(List<EditOp> ops)
    {
        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            if ((op.Kind == EditOpKind.InsertSubtree
                 || op.Kind == EditOpKind.RemoveSubtree
                 || op.Kind == EditOpKind.MoveSubtree)
                && !op.Trusted)
            {
                return false;
            }
        }

        return true;
    }

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
