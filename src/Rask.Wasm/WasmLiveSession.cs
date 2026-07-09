using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Authorization;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Wasm;

// Render/diff/payload pipeline + the IJSRuntime queue live in LiveSessionBase (Core), shared with
// the Server host. WasmLiveSession adds the in-process transport: the JSImport ApplyRender push,
// the single dispatch lock, the route-auth guard, the navigate/dispatch handlers, and the byte[]-
// per-frame model the JSExport boundary needs.
internal sealed class WasmLiveSession : LiveSessionBase, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Held so Dispose can unsubscribe symmetrically. The provider can outlive the session (it is a
    // separate service), so a dangling subscription would fire OnUserChanged on a disposed session
    // (disposed _lock → ObjectDisposedException). In the normal single-session-per-page lifetime
    // Dispose is never called, but keeping the teardown correct guards tests and any future
    // multi-session / re-init path.
    private readonly IUserProvider? _userProvider;

    // Host transport for LiveSessionBase.TryEmitFrameAsync: push the built frame to JS zero-copy via a
    // MemoryView over the write buffer. The JS applyRender reads the bytes synchronously (decode +
    // JSON.parse) within this call, so the view never outlives it — hence a synchronous, completed
    // ValueTask (no interop-boundary await on the browser's single thread).
    protected override ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame)
    {
        JSInterop.ApplyRender(MemoryMarshal.AsMemory(frame).Span);
        return default;
    }

    // Set by BuildPayloadAsync when the frame it built carries queued IJSRuntime calls. The
    // publish-render noop guard must NOT drop such a frame even when the HTML is unchanged — the
    // invokes still need to reach the client (where they run after applyDiff).
    private bool _lastBuildHadJsInvokes;

    public WasmLiveSession(Component view, IServiceProvider services)
        : base(view, services)
    {
        // Bind this session to the runtime so its BeginInvokeJS queues onto JsInvokes.
        (services.GetService<WasmJSRuntime>())?.AttachHost(this);

        if (services.GetService<IUserProvider>() is { } userProvider)
        {
            _userProvider = userProvider;
            userProvider.Changed += OnUserChanged;
        }
    }

    public void Dispose()
    {
        // Unsubscribe first so a late Changed can't fire OnUserChanged on the now-disposed _lock.
        if (_userProvider is not null)
        {
            _userProvider.Changed -= OnUserChanged;
        }

        ComponentLifecycle.DisposeComponentTree(View);
        _lock.Dispose();
    }

    protected override async Task RenderInScopeCoreAsync()
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
            await BuildPayloadAsync(null, false).ConfigureAwait(false);
            if (await TryEmitFrameAsync(false).ConfigureAwait(false))
            {
                _htmlBuffers.Commit();
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevCtx);
        }
    }

    protected override async Task RequestRenderInternalAsync(bool publishOnly)
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
            await BuildPayloadCoalescingRerendersAsync(null, false, publishOnly)
                .ConfigureAwait(false);

            // Noop publish-render guard: an auto-publish triggered by a completed
            // OnRenderedAsync that didn't mutate any tracked state produces the
            // same HTML. Sending it forces the JS side to morph identical HTML,
            // which strips any DOM state JS applied between the previous frame
            // and now — most visibly the `.hljs` class hljs added during the
            // previous frame's dispatch. Skip such frames entirely.
            if (publishOnly
                && !_lastBuildHadJsInvokes
                && _htmlBuffers.CurrentEqualsPrevious())
            {
                return;
            }

            // Push the built frame unless it's byte-identical to the last sent frame. EmitFrame's
            // double-buffered span compare (SIMD-accelerated) catches StateHasChanged calls that
            // didn't change visible output — no per-frame byte[].
            if (await TryEmitFrameAsync(false).ConfigureAwait(false))
            {
                _htmlBuffers.Commit();
            }
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
        }
    }

    private void OnUserChanged() => _ = RequestRenderAsync();

    public async Task<byte[]> InitialRenderAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            await BuildPayloadAsync(null, false).ConfigureAwait(false);
            if (!await TryEmitFrameAsync(true).ConfigureAwait(false))
            {
                return Array.Empty<byte>();
            }

            _htmlBuffers.Commit();
            // The bootstrap caller wants the initial frame bytes; ToArray once per page load.
            return _lastSentBuffer!.WrittenSpan.ToArray();
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

                    await BuildPayloadCoalescingRerendersAsync(historyUrl, historyReplace)
                        .ConfigureAwait(false);
                    // EmitFrame pushes the frame (zero-copy) unless it's byte-identical to the last
                    // sent one; force the send when a navigation URL must flow even if the rendered
                    // output is unchanged. No send → no-op (empty byte array to the test seam).
                    if (!await TryEmitFrameAsync(historyUrl is not null).ConfigureAwait(false))
                    {
                        return Array.Empty<byte>();
                    }

                    _htmlBuffers.Commit();
                    // Return the sent frame's bytes for the test seam; ToArray once per event.
                    return _lastSentBuffer!.WrittenSpan.ToArray();
                }
            }
            catch (Exception ex)
            {
                RaskDiagnostics.Report(
                    RaskLogLevel.Error,
                    "Rask.Wasm",
                    $"Rask WASM handler '{handlerId}' threw",
                    ex);
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
                await BuildPayloadCoalescingRerendersAsync(fullUrl, replace).ConfigureAwait(false);
                // Navigation always flows — force the send even when the rendered output is unchanged.
                if (!await TryEmitFrameAsync(true).ConfigureAwait(false))
                {
                    return Array.Empty<byte>();
                }

                _htmlBuffers.Commit();
                return _lastSentBuffer!.WrittenSpan.ToArray();
            }
            catch (Exception ex)
            {
                RaskDiagnostics.Report(
                    RaskLogLevel.Error,
                    "Rask.Wasm",
                    $"Rask WASM navigate '{navPath}' threw",
                    ex);
                return Array.Empty<byte>();
            }
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
        }
    }

    private async Task BuildPayloadCoalescingRerendersAsync(string? historyUrl,
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
        // commitCache:false — every iteration diffs against the SAME (last-sent) baseline
        // rather than promoting its own render. Only the LAST build is sent, so committing each
        // intermediate rotation would make the final build diff against an un-sent render —
        // shipping a tiny stale diff (e.g. a head-only diff after a real page swap) that never
        // updates the body. We commit the final render once, after the loop. Each rebuild
        // overwrites _htmlBuffers.Current, so it holds the final render when the loop settles.
        _pendingRenderInScope = false;
        await BuildPayloadAsync(historyUrl, replace, publishOnly, false).ConfigureAwait(false);
        var budget = 2;
        while (_pendingRenderInScope && budget-- > 0)
        {
            _pendingRenderInScope = false;
            await BuildPayloadAsync(historyUrl, replace, publishOnly, false).ConfigureAwait(false);
        }

        // Commit the final, actually-sent render as the new diff baseline exactly once.
        _renderCache?.Snapshot();

        // Budget exhaustion surfaces a third queued in-dispatch render that won't
        // flush — the next event picks up the trailing state. Match the server-side
        // telemetry so dispatch-render-loop bugs are greppable on either runtime.
        if (_pendingRenderInScope)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Warning,
                "Rask.Wasm",
                "[Rask.WasmLiveSession] Coalesce-loop budget exhausted; a third " +
                "in-dispatch render was queued and dropped. Inspect any handlers " +
                "that re-trigger StateHasChanged in OnRenderedAsync / dispose " +
                "callbacks during this dispatch.");
        }
    }

    // commitCache=false defers the render-cache rotation to the caller (the coalescing loop),
    // so intermediate rebuilds diff against the stable last-sent baseline instead of against
    // each other. Only the final, actually-sent build's render becomes the new baseline (the
    // loop calls Snapshot once after it settles). Direct senders (RenderInScopeAsync,
    // InitialRenderAsync) leave it true: their payload reaches the client, so it must commit.
    internal async Task BuildPayloadAsync(string? historyUrl, bool replace,
        bool publishOnly = false, bool commitCache = true)
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
                var originalUrl = QueryString.Build(routeState.Path, routeState.Query);
                var redirectPath = authResult.Outcome == RouteAuthorizationOutcome.Forbid
                    ? RouteAuthorizationGuard.ForbidPath
                    : RouteAuthorizationGuard.ChallengePath;
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

        // Render + decide diff-vs-full + write the frame — shared with the Server host
        // (LiveSessionBase). WASM has no AuthInstruction in the diff codec (the route-auth guard
        // above already redirected), so auth is null; the data-rask-root id is the constant "wasm".
        var html = RenderTreeToHtml(publishOnly, out var frameWriter);
        var download = ConsumeDownload();

        // Drain IJSRuntime calls queued during the render walk (e.g. an OnRenderedAsync focus). They
        // ride this frame's jsInvokes and the client runs them AFTER applyDiff, so they act on the
        // committed DOM. _lastBuildHadJsInvokes lets the caller's noop guard ship this frame even
        // when the HTML is unchanged.
        var jsInvokes = JsInvokes.Drain();
        _lastBuildHadJsInvokes = jsInvokes is not null;

        WritePayload(html, frameWriter, download, jsInvokes, historyUrl, replace,
            commitCache, auth: null, sessionId: "wasm");

        // The built frame lives in _writeBuffer; EmitFrame pushes it zero-copy. The rendered chars stay
        // in _htmlBuffers.Current for the caller's noop-guard / diff-cache baseline (committed on send).
    }
}
