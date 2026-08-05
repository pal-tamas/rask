using System.Buffers;
using System.Reflection.Metadata;
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

    // Set when an in-handler StateHasChanged lands mid-dispatch (InHandlerScope=true); the coalescing
    // loop reads and clears it to rebuild the payload before releasing the dispatch lock.
    protected bool _pendingRenderInScope;

    // The wire-payload shape for THIS session, snapshotted from the host's RaskLiveOptions at
    // construction and read on the render hot path (RenderTreeToHtml / WritePayload) instead of the
    // former process-global LiveOptions.DiffMode static. Per-session so two hosts in one process — and
    // parallel tests — each render in their own mode instead of racing a shared mutable static. Mirrors
    // the per-host RaskServerLimits pattern.
    protected LiveDiffMode DiffMode { get; }

    // Host-awareness axes surfaced to components (Component.HostShell/HostEngine/HostPlatform → LiveRenderContext →
    // these). The IRenderHandle members forward to protected virtuals so the per-host sessions override with
    // plain virtual dispatch. The base defaults describe the default web+server host; WasmLiveSession sets
    // Wasm, NativeLiveSession sets Native/InProcess + the device platform. Constant for the session lifetime.
    RenderShell IRenderHandle.Shell => ShellCore;
    RenderEngine IRenderHandle.Engine => EngineCore;
    RenderPlatform IRenderHandle.Platform => PlatformCore;

    protected virtual RenderShell ShellCore => RenderShell.Web;
    protected virtual RenderEngine EngineCore => RenderEngine.Server;
    protected virtual RenderPlatform PlatformCore => RenderPlatform.None;

    // Native-chrome collection seam — forwarded from IRenderHandle to protected virtuals so the native host
    // overrides them with plain virtual dispatch. Base is a no-op (non-native hosts collect nothing). The
    // component arrives as a plain Component; the native session picks out the native bars (Rask.Core has no
    // Rask.Native type reference).
    bool IRenderHandle.CollectsNativeChrome => CollectsNativeChromeCore;
    void IRenderHandle.ReportNativeComponent(Component component) => ReportNativeComponentCore(component);

    protected virtual bool CollectsNativeChromeCore => false;
    protected virtual void ReportNativeComponentCore(Component component) { }

    // Called at the very start of each render walk (before the tree is serialized). Hosts override to reset
    // per-render collected state (the native host clears its last-collected header/footer so a removed bar
    // drops out). Base is a no-op.
    protected virtual void OnBeforeRenderWalk() { }

    protected LiveSessionBase(Component view, IServiceProvider services, LiveDiffMode diffMode)
    {
        View = view;
        Services = services;
        DiffMode = diffMode;
        view.RenderHandle = this;
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
    private static readonly object _hotReloadLock = new();
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
    internal static async Task RerenderAllForHotReloadAsync()
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

        foreach (var session in alive)
        {
            try
            {
                Component.MarkSubtreeDirtyForHotReload(session.View);
                await session.RequestRenderAsync().ConfigureAwait(false);
            }
            catch
            {
                // Hot reload must never throw; skip a session whose tree walk / render faults.
            }
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
            return false;
        }

        if (!force
            && _lastSentBuffer is not null
            && _writeBuffer.WrittenSpan.SequenceEqual(_lastSentBuffer.WrittenSpan))
        {
            return false;
        }

        await SendFrameAsync(_writeBuffer.WrittenMemory).ConfigureAwait(false);

        (_lastSentBuffer, _writeBuffer) = (_writeBuffer, _lastSentBuffer ?? new ArrayBufferWriter<byte>());
        return true;
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
        frameWriter = null;
        FrameSinkScope.Popper popper = default;
        if (DiffMode != LiveDiffMode.DisabledFull)
        {
            _renderCache ??= new SessionRenderCache();
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

    /// <summary>Consume a one-shot download side-effect produced during the render, if any.</summary>
    protected PendingDownload? ConsumeDownload() =>
        Services.GetService<IDownloadSink>() is { } sink && sink.TryConsume(out var pd) ? pd : null;

    /// <summary>
    ///     A sealed session-resume record to attach to the payload being written, or <c>null</c> for none.
    /// </summary>
    /// <remarks>
    ///     Only the ASP.NET host overrides this. A WASM or native app IS the process holding its session,
    ///     so there is no other host for a record to be redeemed on and nothing to carry — the base returns
    ///     null and those hosts pay a null check per frame. Named "Take" because an override is expected to
    ///     mark the record as issued: it is called once per payload and must not hand out the same record
    ///     on every subsequent frame.
    /// </remarks>
    protected virtual string? TakeResumeToken() => null;

    /// <summary>Bytes the <c>"resume":"…"</c> field name, quotes and separator add around the record itself.</summary>
    private const int ResumeFieldOverhead = 12;

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

        // A session-resume record, when this host issues them and the page has moved since the last one.
        // Taken once per payload — before either branch below — so the diff and full paths carry the same
        // record and a frame that ends up discarded on size doesn't consume it twice. Null on every host
        // but the ASP.NET one, which is the only place a session can outlive the process holding it.
        var resume = TakeResumeToken();

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
            var headChanged = _htmlBuffers.HasPrevious && !LiveDiffGate.HeadUnchanged(html.Span, _htmlBuffers.PreviousSpan);
            // Ship the diff when it carries DOM ops, OR when it carries none but a navigation or a
            // head change must still flow (a query-only nav pushes the URL; a head-only change ships
            // the head fragment). Zero ops + no history + unchanged head means nothing to send.
            if (_renderCache.TryComputeDiff(_diffOps, commitCache, html.Span)
                && (_diffOps.Count > 0 || historyUrl is not null || headChanged)
                && LiveDiffGate.DiffOpsAreClientSupported(_diffOps)
                && !_renderCache.LastDiffForcedFullHtml)
            {
                var headHtml = headChanged ? LiveDiffGate.ExtractHead(html.Span) : null;
                LivePayload.BuildPayloadUtf8Diff(_writeBuffer, _diffOps, historyUrl, replace, jsInvokes,
                    headHtml, html.Span, resume);

                // Ship the diff whenever it isn't larger than re-sending the body, or unconditionally
                // under Forced. Only the pathological case (nearly every node changed on a tiny page,
                // so op-list framing exceeds the body) falls back to full HTML on size.
                //
                // The resume record is discounted from the diff's measured size because the full-HTML
                // payload would carry the identical record: it is on both sides of this comparison, so
                // letting it count only against the diff would flip small pages to full HTML purely
                // because a record happened to be due — a page whose diff is a few hundred bytes would
                // start shipping its whole body every time the declared state moved.
                var resumeCost = resume is null ? 0 : resume.Length + ResumeFieldOverhead;
                if (DiffMode == LiveDiffMode.Forced || _writeBuffer.WrittenCount - resumeCost < html.Length)
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
            // Full-HTML path (first render, structural change, out-of-band side effect, size fallback):
            // this ships the whole body anyway, so materialising it as a string here is not wasted.
            LivePayload.BuildPayloadUtf8WithRoot(_writeBuffer, new string(html.Span), sessionId, historyUrl,
                replace, auth, download, jsInvokes, resume);
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
