using System.Buffers;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Core.Live;

// The transport-independent half of a live session, shared by Rask.Server's LiveSession (WebSocket)
// and Rask.Wasm's WasmLiveSession (in-process JSImport). It owns the render→payload pipeline both
// hosts run identically: render the component tree (capturing the diff frame stream), then decide
// diff-vs-full and write the frame into WriteBuffer. The hosts keep what genuinely differs — their
// transport (WS push vs ApplyRender), their locking, reconnect/dispatch lifecycle, and the dedup
// strategy around the send (Server double-buffers; WASM returns a byte[] each frame).
internal abstract class LiveSessionBase : IRenderHandle, ILiveJsHost
{
    // Pooled across the session lifetime; ResetWrittenCount between frames keeps the rented backing
    // array hot. Non-readonly: Server swaps it with its previous-frame buffer for zero-copy dedup.
    protected ArrayBufferWriter<byte> _writeBuffer = new(4096);

    // Diff-codec state, lazily allocated only when LiveOptions.DiffMode opts in — the default
    // (DisabledFull) path pays nothing for these.
    protected List<EditOp>? _diffOps;
    protected SessionRenderCache? _renderCache;

    // Last rendered HTML, the dedup baseline that suppresses noop renders which would otherwise
    // re-morph identical HTML and strip JS-applied DOM state (e.g. hljs's `.hljs` class).
    protected string? _lastAppliedHtml;

    // Set when an in-handler StateHasChanged lands mid-dispatch (InHandlerScope=true); the coalescing
    // loop reads and clears it to rebuild the payload before releasing the dispatch lock.
    protected bool _pendingRenderInScope;

    protected LiveSessionBase(Component view, IServiceProvider services)
    {
        View = view;
        Services = services;
        view.RenderHandle = this;
        // RootErrorBoundary wraps the user's App; forward the handle to the inner so its
        // StateHasChanged() reaches the session even before the first GetOrCreate would attach it.
        if (view is RootErrorBoundary root)
        {
            root.Inner.RenderHandle = this;
        }
    }

    public Component View { get; }
    public IServiceProvider Services { get; }

    // Pending IJSRuntime calls, drained into each frame's jsInvokes and dispatched client-side after
    // applyDiff (post-commit ordering). Shared queue type so both hosts order interop identically.
    public LiveJsInvokeQueue JsInvokes { get; } = new();

    // Plain instance bool, NOT AsyncLocal: the dispatch lock is owned by the session as a whole, not
    // by any one async chain. AsyncLocal would flow into Timer/Task captures created during a render
    // and later report InHandlerScope=true forever, stranding background StateHasChanged calls.
    public bool InHandlerScope { get; set; }

    public Task RequestRenderAsync() => RequestRenderInternalAsync(false);

    public Task RequestPublishRenderAsync() => RequestRenderInternalAsync(true);

    Task IRenderHandle.RenderInScopeAsync() => RenderInScopeCoreAsync();

    protected abstract Task RequestRenderInternalAsync(bool publishOnly);

    /// <summary>The framework's mid-await intermediate render (see Component.InvokeWithRenderingAsync).</summary>
    protected abstract Task RenderInScopeCoreAsync();

    /// <summary>
    ///     Render the component tree to HTML, capturing the parallel <c>RenderFrame</c> stream when
    ///     the diff codec is on (so <see cref="WritePayload" /> can emit a minimal edit-op payload).
    ///     Default (DisabledFull) bypasses entirely — null frame sink, a single null check per
    ///     HtmlSerializer branch.
    /// </summary>
    protected string RenderTreeToHtml(bool publishOnly, out FrameWriter? frameWriter)
    {
        frameWriter = null;
        FrameSinkScope.Popper popper = default;
        if (LiveOptions.DiffMode != LiveDiffMode.DisabledFull)
        {
            _renderCache ??= new SessionRenderCache();
            frameWriter = _renderCache.PrepareCurrentBuffer();
            popper = FrameSinkScope.Push(frameWriter);
        }

        try
        {
            return View.RenderAsLiveRoot(Services, publishOnly);
        }
        finally
        {
            if (frameWriter is not null)
            {
                popper.Dispose();
            }
        }
    }

    /// <summary>Consume a one-shot download side-effect produced during the render, if any.</summary>
    protected PendingDownload? ConsumeDownload() =>
        Services.GetService<IDownloadSink>() is { } sink && sink.TryConsume(out var pd) ? pd : null;

    /// <summary>
    ///     Decide diff-vs-full for the just-rendered <paramref name="html" /> and write the frame
    ///     into <see cref="_writeBuffer" />. Identical on both hosts; the seams are parameters:
    ///     <paramref name="auth" /> (Server only — gates the diff path and rides the full payload;
    ///     WASM passes null), <paramref name="sessionId" /> (the data-rask-root value), and
    ///     <paramref name="commitCache" /> (false during a coalescing loop, so intermediate rebuilds
    ///     diff against the stable last-sent baseline and the caller Snapshots once after).
    /// </summary>
    protected void WritePayload(string html, FrameWriter? frameWriter, PendingDownload? download,
        PendingJsInvoke[]? jsInvokes, string? historyUrl, bool replace, bool commitCache,
        AuthInstruction? auth, string sessionId)
    {
        _writeBuffer.ResetWrittenCount();

        // Out-of-band side effects (auth, download) and structural ops route to the full-HTML morph
        // path; in-place state changes ship a diff. Head changes (per-route <title>, a scoped-asset
        // <link>, a reactive title) ride the diff as an attached <head> fragment since the diff
        // frame stream never carries <head>. Genuine body-restructuring page swaps fall back to full
        // HTML via DiffOpsAreClientSupported. jsInvokes do NOT force full HTML — they ride the diff.
        var usedDiff = false;
        var diffPathEntered = false;
        if (frameWriter is not null && _renderCache is not null && auth is null && download is null)
        {
            _diffOps ??= new List<EditOp>();
            diffPathEntered = true;
            var headChanged = _lastAppliedHtml is not null && !LiveDiffGate.HeadUnchanged(html, _lastAppliedHtml);
            // Ship the diff when it carries DOM ops, OR when it carries none but a navigation or a
            // head change must still flow (a query-only nav pushes the URL; a head-only change ships
            // the head fragment). Zero ops + no history + unchanged head means nothing to send.
            if (_renderCache.TryComputeDiff(_diffOps, commitCache, html)
                && (_diffOps.Count > 0 || historyUrl is not null || headChanged)
                && LiveDiffGate.DiffOpsAreClientSupported(_diffOps))
            {
                var headHtml = headChanged ? LiveDiffGate.ExtractHead(html) : null;
                LivePayload.BuildPayloadUtf8Diff(_writeBuffer, _diffOps, historyUrl, replace, jsInvokes,
                    headHtml, html);

                // Ship the diff whenever it isn't larger than re-sending the body, or unconditionally
                // under Forced. Only the pathological case (nearly every node changed on a tiny page,
                // so op-list framing exceeds the body) falls back to full HTML on size.
                if (LiveOptions.DiffMode == LiveDiffMode.Forced || _writeBuffer.WrittenCount < html.Length)
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
            LivePayload.BuildPayloadUtf8WithRoot(_writeBuffer, html, sessionId, historyUrl, replace,
                auth, download, jsInvokes);
            // Keep the cache in lockstep with the client even when shipping full HTML: promote
            // current → previous so the NEXT diff's baseline matches what the client received. Skip
            // when TryComputeDiff already rotated (diffPathEntered), or when the caller defers the
            // commit to the coalescing loop (commitCache=false).
            if (!diffPathEntered && commitCache)
            {
                _renderCache?.Snapshot();
            }
        }
    }
}
