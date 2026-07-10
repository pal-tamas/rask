using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Authorization;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Native;

// The render/diff/payload pipeline + the IJSRuntime queue live in LiveSessionBase (Core), shared with
// the Server and WASM hosts. NativeLiveSession adds the in-process native transport: it pushes each
// built frame to the platform WebView through INativeWebView.ApplyRenderAsync, holds a single dispatch
// lock, runs the route-auth guard inline (no server round-trip — like WASM), and turns WebView events
// into handler/navigate dispatches. Structurally a near-mirror of Rask.Wasm.WasmLiveSession; the only
// real difference is the transport (a WebView bridge instead of the WASM JSImport ApplyRender).
internal sealed class NativeLiveSession : LiveSessionBase, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Serializes the actual render+emit. Native runs async lifecycle/handler continuations on the thread
    // pool (HandlerSyncContext.Post uses Task.Run), so a mid-await render (RenderInScopeCoreAsync, or a
    // second continuation's render) can fire concurrently with the dispatch's render — and two renders
    // walking the component tree at once race ComponentLifecycle.DisposeComponentTree's PersistedChildren
    // enumeration ("Collection was modified; enumeration operation may not execute"), which trips the root
    // error boundary. Server has the same _renderLock; WASM is single-threaded so it needs none. It's held
    // only around one build+emit (never across a handler/await), so the legitimate re-entrant case —
    // InvokeWithRenderingAsync rendering inline inside a handler, then the dispatch's own render afterwards
    // — stays sequential (each acquires a free lock). Lock order is always _lock (if any) then _renderLock;
    // RenderInScopeCoreAsync takes only _renderLock, so there's no inversion.
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private readonly INativeWebView _webView;
    private readonly IUserProvider? _userProvider;

    // Set by BuildPayloadAsync when the frame it built carries queued IJSRuntime calls. The
    // publish-render noop guard must NOT drop such a frame even when the HTML is unchanged — the
    // invokes still need to reach the client (where they run after applyDiff).
    private bool _lastBuildHadJsInvokes;

    public NativeLiveSession(Component view, IServiceProvider services, INativeWebView webView, LiveDiffMode diffMode)
        : base(view, services, diffMode)
    {
        _webView = webView;

        // Bind this session to the runtime so its BeginInvokeJS queues onto JsInvokes.
        services.GetService<NativeJSRuntime>()?.AttachHost(this, webView);

        if (services.GetService<IUserProvider>() is { } userProvider)
        {
            _userProvider = userProvider;
            userProvider.Changed += OnUserChanged;
        }
    }

    public void Dispose()
    {
        if (_userProvider is not null)
        {
            _userProvider.Changed -= OnUserChanged;
        }

        ComponentLifecycle.DisposeComponentTree(View);
        _lock.Dispose();
        _renderLock.Dispose();
    }

    // Host transport for LiveSessionBase.TryEmitFrameAsync: hand the built frame to the platform WebView,
    // whose window.__raskNative.applyRender consumes it (applyDiff / morph). The memory is valid until the
    // returned ValueTask completes (the base awaits SendFrameAsync before swapping buffers), so a UI-thread
    // hop inside the platform implementation is safe.
    protected override ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame) => _webView.ApplyRenderAsync(frame);

    protected override async Task RenderInScopeCoreAsync()
    {
        // Mirror WasmLiveSession: when the framework asks for a mid-await render
        // (Component.InvokeWithRenderingAsync), build and push an intermediate payload directly so
        // transient UI state (e.g. an async-validator "Checking…" indicator) reaches the WebView before
        // the post-handler payload supersedes it. The dispatcher already holds _lock. Suppress the ambient
        // SynchronizationContext (HandlerSyncContext) for the duration so BuildPayloadAsync's internal
        // `await Task.Yield()` can't Post its continuation back through it and re-enter this method.
        var prevCtx = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        await _renderLock.WaitAsync().ConfigureAwait(false);
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
            _renderLock.Release();
            SynchronizationContext.SetSynchronizationContext(prevCtx);
        }
    }

    protected override async Task RequestRenderInternalAsync(bool publishOnly)
    {
        if (InHandlerScope)
        {
            _pendingRenderInScope = true;
            return;
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        await _renderLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await BuildPayloadCoalescingRerendersAsync(null, false, publishOnly).ConfigureAwait(false);

            // Noop publish-render guard: an auto-publish from a completed OnRenderedAsync that didn't
            // mutate tracked state produces identical HTML; morphing it would strip DOM state JS applied
            // between frames. Skip such frames unless they carry queued IJSRuntime calls.
            if (publishOnly && !_lastBuildHadJsInvokes && _htmlBuffers.CurrentEqualsPrevious())
            {
                return;
            }

            if (await TryEmitFrameAsync(false).ConfigureAwait(false))
            {
                _htmlBuffers.Commit();
            }
        }
        finally
        {
            _renderLock.Release();
            InHandlerScope = false;
            _lock.Release();
        }
    }

    private void OnUserChanged() => _ = RequestRenderAsync();

    /// <summary>
    ///     Build and push the first frame (a full-HTML morph onto <c>document.documentElement</c>). Called
    ///     once at boot from <see cref="NativeAppHost" />. Returns the sent bytes for diagnostics/tests.
    /// </summary>
    public async Task<byte[]> InitialRenderAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        await _renderLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await BuildPayloadAsync(null, false).ConfigureAwait(false);
            if (!await TryEmitFrameAsync(true).ConfigureAwait(false))
            {
                return Array.Empty<byte>();
            }

            _htmlBuffers.Commit();
            return _lastSentBuffer!.WrittenSpan.ToArray();
        }
        finally
        {
            _renderLock.Release();
            InHandlerScope = false;
            _lock.Release();
        }
    }

    /// <summary>
    ///     Handle one WebView event message (a component <c>id</c>-carrying handler event, or a
    ///     <c>navigate</c>). Mirrors <c>WasmLiveSession.DispatchAsync</c>: parse the UTF-8 JSON, route,
    ///     invoke the handler, render, and push the frame. Returns the sent frame bytes (the test seam;
    ///     production also pushes them via <see cref="SendFrameAsync" />). <c>jsResult</c>/<c>dotNetInvoke</c>
    ///     messages are handled upstream by <see cref="NativeAppHost" /> before they reach here.
    /// </summary>
    public async Task<byte[]> DispatchAsync(byte[] json)
    {
        if (json is null || json.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var doc = JsonDocument.Parse(json.AsMemory());
        var root = doc.RootElement.Clone();

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

                    // Acquire _renderLock only around the render (not the handler above): an in-handler
                    // InvokeWithRenderingAsync renders inline under _renderLock first, so holding it across
                    // the handler would deadlock.
                    await _renderLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        await BuildPayloadCoalescingRerendersAsync(historyUrl, historyReplace).ConfigureAwait(false);
                        if (!await TryEmitFrameAsync(historyUrl is not null).ConfigureAwait(false))
                        {
                            return Array.Empty<byte>();
                        }

                        _htmlBuffers.Commit();
                        return _lastSentBuffer!.WrittenSpan.ToArray();
                    }
                    finally
                    {
                        _renderLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                RaskDiagnostics.Report(
                    RaskLogLevel.Error, "Rask.Native", $"Rask native handler '{handlerId}' threw", ex);
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

            await _renderLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await BuildPayloadCoalescingRerendersAsync(fullUrl, replace).ConfigureAwait(false);
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
                    RaskLogLevel.Error, "Rask.Native", $"Rask native navigate '{navPath}' threw", ex);
                return Array.Empty<byte>();
            }
            finally
            {
                _renderLock.Release();
            }
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
        }
    }

    private async Task BuildPayloadCoalescingRerendersAsync(string? historyUrl, bool replace, bool publishOnly = false)
    {
        // Rebuild while in-dispatch StateHasChanged calls keep landing (dispose callbacks, an
        // OnRenderedAsync continuation) so the returned payload carries the settled state. Only the LAST
        // build is sent; commitCache:false keeps every iteration diffing against the stable last-sent
        // baseline, and the final render is committed once after the loop. See WasmLiveSession for the
        // full rationale (navigation-target re-pass, budget exhaustion telemetry).
        _pendingRenderInScope = false;
        await BuildPayloadAsync(historyUrl, replace, publishOnly, false).ConfigureAwait(false);
        var budget = 2;
        while (_pendingRenderInScope && budget-- > 0)
        {
            _pendingRenderInScope = false;
            await BuildPayloadAsync(historyUrl, replace, publishOnly, false).ConfigureAwait(false);
        }

        _renderCache?.Snapshot();

        if (_pendingRenderInScope)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Warning, "Rask.Native",
                "[Rask.NativeLiveSession] Coalesce-loop budget exhausted; a third in-dispatch render was " +
                "queued and dropped. Inspect handlers that re-trigger StateHasChanged in OnRenderedAsync / " +
                "dispose callbacks during this dispatch.");
        }
    }

    internal async Task BuildPayloadAsync(string? historyUrl, bool replace, bool publishOnly = false, bool commitCache = true)
    {
        await Task.Yield();

        var routeState = Services.GetRequiredService<RouteState>();
        if (RouteResolver.TryResolve(routeState.Path, out var chain))
        {
            var user = Services.GetService<IUserProvider>()?.Current
                       ?? new ClaimsPrincipal(new ClaimsIdentity());
            var authResult = await RouteAuthorizationGuard.EvaluateAsync(Services, chain, user).ConfigureAwait(false);
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

        // Render + decide diff-vs-full + write the frame — shared with the Server/WASM hosts. Native has
        // no AuthInstruction in the diff codec (the route-auth guard above already redirected), so auth is
        // null; the data-rask-root id is the constant "native".
        var html = RenderTreeToHtml(publishOnly, out var frameWriter);
        var download = ConsumeDownload();

        var jsInvokes = JsInvokes.Drain();
        _lastBuildHadJsInvokes = jsInvokes is not null;

        WritePayload(html, frameWriter, download, jsInvokes, historyUrl, replace,
            commitCache, auth: null, sessionId: "native");
    }
}
