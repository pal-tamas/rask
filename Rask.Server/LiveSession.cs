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
    private static readonly AsyncLocal<bool> _inHandlerScope = new();

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
    private WebSocket? _socket;
    private CancellationToken _socketCt;

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

    public bool InHandlerScope
    {
        get => _inHandlerScope.Value;
        set => _inHandlerScope.Value = value;
    }

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
            return;
        }

        if (InHandlerScope)
        {
            await RenderAndSendAsync(null, false, publishOnly: publishOnly).ConfigureAwait(false);
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

    public void AttachSocket(WebSocket socket, CancellationToken ct)
    {
        _socket = socket;
        _socketCt = ct;
        SuppressEventsUntilReconnect = false;
        // A reconnect — possibly a different browser tab/window — needs the current HTML
        // even when it byte-matches the prior socket's last frame. Reset the dedup baseline
        // so the recovery render reliably emits.
        _lastSentBuffer = null;
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
            var html = View.RenderAsLiveRoot(Services, publishOnly);

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

            // Noop publish-render guard: an auto-publish triggered by a completed
            // OnRenderedAsync that didn't mutate any tracked state produces the
            // same HTML and has no invokes to ship. Sending it anyway forces the
            // client to morph identical HTML, which strips out any DOM state JS
            // applied between the previous frame and now — most visibly the
            // `.hljs` class hljs added to <code> elements during the previous
            // frame's dispatch. Skip such frames entirely.
            if (publishOnly && jsInvokes is null
                && historyUrl is null && auth is null && download is null
                && _lastSentHtml is not null && string.Equals(html, _lastSentHtml, StringComparison.Ordinal))
            {
                return;
            }

            _writeBuffer.ResetWrittenCount();
            LivePayload.BuildPayloadUtf8WithRoot(_writeBuffer, html, Id, historyUrl, replace, null, auth, download,
                jsInvokes: jsInvokes);

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
