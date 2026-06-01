using System.Buffers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Authorization;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Core.ScopedCss;
using Rask.Core.ScopedJs;

namespace Rask.Wasm;

internal sealed class WasmLiveSession : IRenderHandle, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Pooled across the session lifetime; ResetWrittenCount between frames keeps the rented
    // backing array hot. Eliminates the per-render ArrayBufferWriter allocation that
    // BuildPayloadUtf8WithRoot's byte[]-returning overload would otherwise make.
    private readonly ArrayBufferWriter<byte> _writeBuffer = new(4096);
    private byte[]? _lastAppliedPayload;
    // Last rendered HTML string, used to suppress noop publish-renders that
    // would otherwise re-morph identical HTML and strip JS-applied DOM state
    // (e.g. the `.hljs` class hljs added in a prior frame's OnRenderedAsync).
    // Set after a successful ApplyRender.
    private string? _lastAppliedHtml;

    // Plain instance bool, NOT AsyncLocal: the dispatch lock is owned by this session as a whole,
    // not by any one async chain. AsyncLocal would flow into Timer/Task captures created during a
    // render — those captured ExecutionContexts would later report InHandlerScope=true forever,
    // making background StateHasChanged calls (e.g. from a Timer in a user component) silently no-op.
    //
    // Note: the historical _lastCssHashSent / _lastJsHashSent fields were removed when WASM
    // moved off inline cssText/jsText payloads to the per-component asset endpoint.
    // Scoped CSS/JS is now fetched by the browser via <link>/<script src> tags emitted into
    // <head> by HeadAssetRegistry.EmitMountedAssets and served by Rask.Wasm.Hosting's
    // /_rask/a/{hash}.{ext} endpoint with Cache-Control: immutable.

    // Diff-codec state, lazily allocated only when LiveOptions.DiffMode opts in.
    // Default path (DisabledFull) pays nothing for these.
    private SessionRenderCache? _renderCache;
    private List<EditOp>? _diffOps;

    // Set by RequestRenderAsync when called while InHandlerScope=true. The dispatch helpers
    // (BuildPayloadCoalescingRerendersAsync) read and clear it to rebuild the payload before
    // releasing the lock — catches state mutated by dispose callbacks that fire AFTER ToHtml
    // serialised the first payload but BEFORE the dispatch returns.
    private bool _pendingRenderInScope;

    public WasmLiveSession(Component view, IServiceProvider services)
    {
        View = view;
        Services = services;
        view.RenderHandle = this;
        // Forward the handle to the inner App when wrapped in a RootErrorBoundary so its
        // StateHasChanged() reaches the session even before the first GetOrCreate would
        // otherwise lazily attach it.
        if (view is RootErrorBoundary root)
        {
            root.Inner.RenderHandle = this;
        }

        if (services.GetService<IUserProvider>() is { } userProvider)
        {
            userProvider.Changed += OnUserChanged;
        }
    }

    public Component View { get; }
    public IServiceProvider Services { get; }

    public bool InHandlerScope { get; set; }

    public void Dispose()
    {
        ComponentLifecycle.DisposeComponentTree(View);
        _lock.Dispose();
    }

    public Task RequestRenderAsync() => RequestRenderInternalAsync(publishOnly: false);

    public Task RequestPublishRenderAsync() => RequestRenderInternalAsync(publishOnly: true);

    private async Task RequestRenderInternalAsync(bool publishOnly)
    {
        if (InHandlerScope)
        {
            // Already inside a dispatch's render: signal the helper to rebuild the payload
            // before releasing the lock so dispose-driven state changes (e.g. a token.Register
            // callback that calls StateHasChanged on a parent) land in the response payload
            // instead of waiting for the next event.
            _pendingRenderInScope = true;
            return;
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            var (payload, html) = await BuildPayloadCoalescingRerendersAsync(null, false, publishOnly)
                .ConfigureAwait(false);

            // Noop publish-render guard: an auto-publish triggered by a completed
            // OnRenderedAsync that didn't mutate any tracked state produces the
            // same HTML. Sending it forces the JS side to morph identical HTML,
            // which strips any DOM state JS applied between the previous frame
            // and now — most visibly the `.hljs` class hljs added during the
            // previous frame's dispatch. Skip such frames entirely.
            if (publishOnly
                && _lastAppliedHtml is not null
                && string.Equals(html, _lastAppliedHtml, StringComparison.Ordinal))
            {
                return;
            }

            // Skip the JS interop call when nothing changed since the last apply. Catches
            // StateHasChanged calls that didn't ultimately modify any visible output.
            // SequenceEqual is SIMD-accelerated on byte[] — equivalent to the prior string compare.
            if (_lastAppliedPayload is not null && payload.AsSpan().SequenceEqual(_lastAppliedPayload))
            {
                return;
            }

            JSInterop.ApplyRender(payload);
            _lastAppliedPayload = payload;
            _lastAppliedHtml = html;
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
        }
    }

    async Task IRenderHandle.RenderInScopeAsync()
    {
        // Mirror Rask.Server LiveSession.RenderAndSendAsync: when the framework asks for a
        // mid-await render (Component.InvokeWithRenderingAsync), build and push an intermediate
        // payload directly via JSInterop.ApplyRender so transient UI state — e.g. the async-
        // validator "Checking…" indicator that lives only during the validator's await window —
        // reaches the browser before the post-handler payload supersedes it. The dispatcher
        // already holds _lock and has InHandlerScope=true at this point, so no re-locking.
        //
        // Suppress the ambient SynchronizationContext (HandlerSyncContext, installed by
        // Component.InvokeWithRenderingAsync) for the duration of this call: BuildPayloadAsync's
        // internal `await Task.Yield()` would otherwise Post its continuation back through
        // HandlerSyncContext, which re-enters this same method via _render — a render loop on
        // the WASM JS task queue.
        var prevCtx = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var (payload, html) = await BuildPayloadAsync(null, false).ConfigureAwait(false);
            if (_lastAppliedPayload is not null && payload.AsSpan().SequenceEqual(_lastAppliedPayload))
            {
                return;
            }

            JSInterop.ApplyRender(payload);
            _lastAppliedPayload = payload;
            _lastAppliedHtml = html;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevCtx);
        }
    }

    private void OnUserChanged() => _ = RequestRenderAsync();

    public async Task<byte[]> InitialRenderAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            var (payload, html) = await BuildPayloadAsync(null, false).ConfigureAwait(false);
            _lastAppliedPayload = payload;
            _lastAppliedHtml = html;
            return payload;
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
        }
    }

    public async Task<byte[]> DispatchAsync(byte[] json)
    {
        // Push model: produce the render payload, then either return it to the caller
        // (tests) OR call JSInterop.ApplyRender from inside .NET (production). The JSExport
        // generator doesn't support Task<byte[]> as a return type — Task<string> works but
        // would force a base64 round-trip — so production callers use the push side.
        // Returning bytes preserves the test seam: tests can assert against the payload
        // without standing up a JS interop bridge.
        if (json is null || json.Length == 0)
        {
            return Array.Empty<byte>();
        }

        JsonElement root;
        // Parse straight from the UTF-8 byte payload — no UTF-16 string materialisation.
        // JS hands the bytes across the interop boundary directly via TextEncoder.encode
        // on the send path, replacing the prior JSON.stringify + string-marshalled call.
        using var doc = JsonDocument.Parse(json.AsMemory());
        root = doc.RootElement.Clone();

        var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;

        if (type == "navigate")
        {
            return await HandleNavigateAsync(root).ConfigureAwait(false);
        }

        var handlerId = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString()
            : null;
        if (handlerId is null)
        {
            return Array.Empty<byte>();
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            var navigator = Services.GetRequiredService<Navigator>();
            try
            {
                using (navigator.EnterHandler())
                {
                    if (!await View.TryInvokeHandlerAsync(handlerId, root, Services).ConfigureAwait(false))
                    {
                        return Array.Empty<byte>();
                    }

                    string? historyUrl = null;
                    var historyReplace = false;
                    if (navigator.TryConsumeHistory(out var url, out var replace))
                    {
                        historyUrl = url;
                        historyReplace = replace;
                    }

                    var (payload, html) = await BuildPayloadCoalescingRerendersAsync(historyUrl, historyReplace)
                        .ConfigureAwait(false);
                    // Suppress the JS-side apply when nothing changed AND no navigation needs to flow.
                    // The JS host treats an empty byte array as "no-op." Always send when historyUrl is set.
                    if (historyUrl is null
                        && _lastAppliedPayload is not null
                        && payload.AsSpan().SequenceEqual(_lastAppliedPayload))
                    {
                        return Array.Empty<byte>();
                    }

                    _lastAppliedPayload = payload;
                    _lastAppliedHtml = html;
                    return payload;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Rask WASM handler '{handlerId}' threw: {ex}");
                return Array.Empty<byte>();
            }
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
        }
    }

    private async Task<byte[]> HandleNavigateAsync(JsonElement root)
    {
        var navPath = root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
        if (string.IsNullOrEmpty(navPath))
        {
            return Array.Empty<byte>();
        }

        var navQueryString = root.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String
            ? q.GetString() ?? string.Empty
            : string.Empty;
        var replace = root.TryGetProperty("replace", out var rEl) && rEl.ValueKind == JsonValueKind.True;

        var fullUrl = string.IsNullOrEmpty(navQueryString)
            ? navPath
            : navQueryString.StartsWith("?", StringComparison.Ordinal)
                ? navPath + navQueryString
                : navPath + "?" + navQueryString;

        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            var routeState = Services.GetRequiredService<RouteState>();
            routeState.Path = navPath;
            routeState.Query = QueryString.Parse(navQueryString);

            try
            {
                var (payload, html) = await BuildPayloadCoalescingRerendersAsync(fullUrl, replace).ConfigureAwait(false);
                _lastAppliedPayload = payload;
                _lastAppliedHtml = html;
                return payload;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Rask WASM navigate '{navPath}' threw: {ex}");
                return Array.Empty<byte>();
            }
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
        }
    }

    private async Task<(byte[] Payload, string Html)> BuildPayloadCoalescingRerendersAsync(string? historyUrl,
        bool replace, bool publishOnly = false)
    {
        // First build emits any pending history navigation. If a dispose callback (or other
        // synchronous code reached during RenderAsLiveRoot) requested another render via
        // StateHasChanged, it set _pendingRenderInScope=true — rebuild so the dispatch's
        // returned payload carries the post-dispose state.
        //
        // historyUrl/replace flow through every rebuild: only the LAST result is returned
        // and sent, so re-passing the captured nav target is the only way for it to land
        // in the actually-emitted frame. Dropping them on the rebuild (as the previous
        // implementation did) silently swallowed handler-initiated navigation whenever a
        // publish-render rebuild fired — e.g. clicking the "Live ticker" sidebar entry
        // triggered LiveTicker.OnRenderedAsync, whose Chart.js-draw continuation requested
        // a publish render via `_pendingRenderInScope`; the rebuild then produced a
        // history-less payload and the URL stayed pinned to /index.html even though the
        // page itself routed correctly. There's no "duplicate pushState" risk because the
        // historyUrl here is a local captured before the call, not a fresh navigator
        // consumption — passing it twice still lands exactly one pushState on the client.
        _pendingRenderInScope = false;
        var result = await BuildPayloadAsync(historyUrl, replace, publishOnly).ConfigureAwait(false);
        var budget = 2;
        while (_pendingRenderInScope && budget-- > 0)
        {
            _pendingRenderInScope = false;
            result = await BuildPayloadAsync(historyUrl, replace, publishOnly).ConfigureAwait(false);
        }

        // Budget exhaustion surfaces a third queued in-dispatch render that won't
        // flush — the next event picks up the trailing state. Match the server-side
        // telemetry so dispatch-render-loop bugs are greppable on either runtime.
        if (_pendingRenderInScope)
        {
            Console.Error.WriteLine(
                "[Rask.WasmLiveSession] Coalesce-loop budget exhausted; a third " +
                "in-dispatch render was queued and dropped. Inspect any handlers " +
                "that re-trigger StateHasChanged in OnRenderedAsync / dispose " +
                "callbacks during this dispatch.");
        }

        return result;
    }

    internal async Task<(byte[] Payload, string Html)> BuildPayloadAsync(string? historyUrl, bool replace, bool publishOnly = false)
    {
        await Task.Yield();

        var routeState = Services.GetRequiredService<RouteState>();
        if (RouteResolver.TryResolve(routeState.Path, out var chain))
        {
            var user = Services.GetService<IUserProvider>()?.Current
                       ?? new ClaimsPrincipal(new ClaimsIdentity());
            var authResult = await RouteAuthorizationGuard
                .EvaluateAsync(Services, chain, user)
                .ConfigureAwait(false);
            if (authResult.Outcome != RouteAuthorizationOutcome.Allow)
            {
                var options = Services.GetRequiredService<RaskAuthorizationOptions>();
                var originalUrl = QueryString.Build(routeState.Path, routeState.Query);
                var redirectPath = authResult.Outcome == RouteAuthorizationOutcome.Forbid
                    ? options.ForbidPath
                    : options.ChallengePath;
                routeState.Path = redirectPath;
                if (authResult.Outcome == RouteAuthorizationOutcome.Challenge)
                {
                    routeState.Query = QueryString.Parse("?returnUrl=" + Uri.EscapeDataString(originalUrl));
                    historyUrl = redirectPath + "?returnUrl=" + Uri.EscapeDataString(originalUrl);
                }
                else
                {
                    routeState.Query = QueryCollection.Empty;
                    historyUrl = redirectPath;
                }

                replace = true;
            }
        }

        // Diff-codec path: capture the parallel RenderFrame[] stream during render so
        // we can ship a minimal edit-op payload instead of the whole document. Default
        // (LiveDiffMode.DisabledFull) bypasses entirely — null FrameSinkScope on entry,
        // single null check per HtmlSerializer branch, zero overhead.
        var diffMode = LiveOptions.DiffMode;
        FrameWriter? frameWriter = null;
        FrameSinkScope.Popper framePopper = default;
        if (diffMode != LiveDiffMode.DisabledFull)
        {
            _renderCache ??= new SessionRenderCache();
            frameWriter = _renderCache.PrepareCurrentBuffer();
            framePopper = FrameSinkScope.Push(frameWriter);
        }

        // The App component owns the full page (Doctype + Html + Head + Body). Send the whole
        // document so the JS runtime can morph <head> too — title, stylesheet <link>s, and the
        // scoped-css <link> would otherwise stay frozen at whatever the static index.html shipped.
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

        // CSS/JS no longer ship inline — scoped assets are content-addressed and fetched
        // via /_rask/a/{hash}.{ext} from Rask.Wasm.Hosting. The diff-codec gate below
        // tracks only side effects that still flow out of band (download payloads,
        // navigation history) since the diff wire format doesn't carry them yet.
        PendingDownload? download = null;
        if (Services.GetService<IDownloadSink>() is { } sink && sink.TryConsume(out var pd))
        {
            download = pd;
        }

        _writeBuffer.ResetWrittenCount();

        // Decide payload shape. The diff path fires only when the flag is opted in,
        // we have a prior render to diff against, the diff is supported client-side
        // (no InsertSubtree/RemoveSubtree until HTML fragments wire in), and none of
        // the out-of-band side effects (download, navigation history) need to flow —
        // those aren't carried by the diff wire format yet.
        var usedDiff = false;
        // Conservative gate (mirrors server LiveSession): the diff path covers
        // in-place state changes only. Side effects (download) and structural ops
        // (InsertSubtree/RemoveSubtree) route to the full-HTML morph path — see the
        // server-side DiffOpsAreClientSupported note for the rationale (e2e showed 83/430
        // failures when structural ops bypass morph). History changes (navigation) ride
        // the diff path ONLY when the rendered <head> is byte-identical to the last applied
        // document: the diff codec walks the body but suppresses head-asset frames, so a
        // body-only diff would freeze <head> (wrong <title>, stale scoped-CSS link). When
        // the head changed we fall back to full HTML, which morphs document.documentElement
        // and picks up the head delta. `diffPathEntered` tracks whether we called
        // TryComputeDiff (which internally rotates buffers regardless of return value) so
        // the fallback Snapshot doesn't double-rotate and strand _previous=null.
        var diffPathEntered = false;
        if (frameWriter is not null && _renderCache is not null
            && download is null
            && (historyUrl is null
                || (_lastAppliedHtml is not null && HeadUnchanged(html, _lastAppliedHtml))))
        {
            _diffOps ??= new List<EditOp>();
            diffPathEntered = true;
            // Ship the diff when it carries DOM ops, OR when it carries no ops but a
            // navigation needs to flow — a query-only nav (or any nav that doesn't alter
            // the rendered body) produces zero ops, and a history-only diff (empty ops +
            // history) pushes the URL for a handful of bytes instead of re-sending the
            // whole document.
            if (_renderCache.TryComputeDiff(_diffOps, html)
                && (_diffOps.Count > 0 || historyUrl is not null)
                && DiffOpsAreClientSupported(_diffOps))
            {
                LivePayload.BuildPayloadUtf8Diff(_writeBuffer, _diffOps, historyUrl, replace);
                var diffBytes = _writeBuffer.WrittenCount;

                // Same rule as the server: ship the diff whenever it isn't larger than
                // re-sending the body, or unconditionally under Forced. We only drop the
                // diff bytes and fall through to full HTML in the pathological case where
                // nearly every node changed (tiny page) and the op-list framing exceeds
                // the body itself — then the diff would cost more bytes than the baseline.
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
            // BuildPayloadUtf8WithRoot fuses InjectRootAttr + payload write on UTF-8 bytes,
            // emitting the whole document (head + body) so the JS-side morph against
            // document.documentElement can update head children. Writes into the pooled
            // _writeBuffer so the per-render ArrayBufferWriter allocation is gone; the
            // ToArray at the end is still needed today because the JS interop boundary
            // marshals byte[] (PR6 will swap that for ReadOnlyMemory<byte>).
            LivePayload.BuildPayloadUtf8WithRoot(_writeBuffer, html, "wasm", historyUrl, replace,
                auth: null, download: download);
            // Only Snapshot when TryComputeDiff was NOT called — otherwise it already
            // rotated buffers and a second Snapshot here would strand _previous=null,
            // breaking the next render's diff with a silent NRE inside DispatchAsync's
            // try/catch (manifests as `result is empty` to callers).
            if (!diffPathEntered)
            {
                _renderCache?.Snapshot();
            }
        }

        return (_writeBuffer.WrittenSpan.ToArray(), html);
    }

    // Structural ops route to full-HTML morph unless EditOp.Trusted is set (which the
    // keyed-matching path in FrameDiffer marks for Move/Insert/Remove). See LiveSession's
    // identically-named helper for the rationale on positional-vs-keyed safety.
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

    // True when the <head> region (prefix through the closing </head>) of the freshly
    // rendered document is byte-identical to the baseline last applied. The diff codec
    // walks the body but suppresses head-asset frames (HeadAssetRegistry pushes a null
    // FrameSinkScope), so a body-only diff would freeze <head> — wrong <title>, stale
    // scoped-CSS link. Only when the head is unchanged is it safe to ship diff+history on
    // a navigation; otherwise full HTML morphs document.documentElement and picks up the
    // head delta. Both strings are the same root-attr-free HtmlSerializer output
    // (data-rask-root is spliced onto <body> only at payload-write time), so the
    // comparison is stable. A missing </head> in either string returns false (treat as
    // changed → full HTML), never an unsafe true.
    private static bool HeadUnchanged(string html, string baseline)
    {
        const string headClose = "</head>";
        var a = html.IndexOf(headClose, StringComparison.Ordinal);
        if (a < 0) return false;
        var b = baseline.IndexOf(headClose, StringComparison.Ordinal);
        if (b < 0) return false;
        a += headClose.Length;
        b += headClose.Length;
        if (a != b) return false;
        return html.AsSpan(0, a).SequenceEqual(baseline.AsSpan(0, b));
    }
}
