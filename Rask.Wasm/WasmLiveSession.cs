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

namespace Rask.Wasm;

internal sealed class WasmLiveSession : IRenderHandle, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    // Pooled across the session lifetime; ResetWrittenCount between frames keeps the rented
    // backing array hot. Eliminates the per-render ArrayBufferWriter allocation that
    // BuildPayloadUtf8WithRoot's byte[]-returning overload would otherwise make.
    private readonly ArrayBufferWriter<byte> _writeBuffer = new(initialCapacity: 4096);
    private byte[]? _lastAppliedPayload;

    // Plain instance bool, NOT AsyncLocal: the dispatch lock is owned by this session as a whole,
    // not by any one async chain. AsyncLocal would flow into Timer/Task captures created during a
    // render — those captured ExecutionContexts would later report InHandlerScope=true forever,
    // making background StateHasChanged calls (e.g. from a Timer in a user component) silently no-op.
    private string? _lastCssHashSent;

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

    public async Task RequestRenderAsync()
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
            var payload = await BuildPayloadCoalescingRerendersAsync(null, false).ConfigureAwait(false);
            // Skip the JS interop call when nothing changed since the last apply. Catches
            // StateHasChanged calls that didn't ultimately modify any visible output.
            // SequenceEqual is SIMD-accelerated on byte[] — equivalent to the prior string compare.
            if (_lastAppliedPayload is not null && payload.AsSpan().SequenceEqual(_lastAppliedPayload))
            {
                return;
            }

            JSInterop.ApplyRender(payload);
            _lastAppliedPayload = payload;
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
            var payload = await BuildPayloadAsync(null, false).ConfigureAwait(false);
            if (_lastAppliedPayload is not null && payload.AsSpan().SequenceEqual(_lastAppliedPayload))
            {
                return;
            }

            JSInterop.ApplyRender(payload);
            _lastAppliedPayload = payload;
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
            var payload = await BuildPayloadAsync(null, false).ConfigureAwait(false);
            _lastAppliedPayload = payload;
            return payload;
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
        }
    }

    public async Task<string> DispatchAsync(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return string.Empty;
        }

        JsonElement root;
        using var doc = JsonDocument.Parse(json);
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
            return string.Empty;
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
                        return string.Empty;
                    }

                    string? historyUrl = null;
                    var historyReplace = false;
                    if (navigator.TryConsumeHistory(out var url, out var replace))
                    {
                        historyUrl = url;
                        historyReplace = replace;
                    }

                    var payload = await BuildPayloadCoalescingRerendersAsync(historyUrl, historyReplace).ConfigureAwait(false);
                    // Suppress the JS-side apply when nothing changed AND no navigation needs to flow.
                    // The JS host treats an empty string as "no-op." Always send when historyUrl is set.
                    if (historyUrl is null
                        && _lastAppliedPayload is not null
                        && payload.AsSpan().SequenceEqual(_lastAppliedPayload))
                    {
                        return string.Empty;
                    }

                    _lastAppliedPayload = payload;
                    return System.Text.Encoding.UTF8.GetString(payload);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Rask WASM handler '{handlerId}' threw: {ex}");
                return string.Empty;
            }
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
        }
    }

    private async Task<string> HandleNavigateAsync(JsonElement root)
    {
        var navPath = root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
        if (string.IsNullOrEmpty(navPath))
        {
            return string.Empty;
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
                var payload = await BuildPayloadCoalescingRerendersAsync(fullUrl, replace).ConfigureAwait(false);
                _lastAppliedPayload = payload;
                return System.Text.Encoding.UTF8.GetString(payload);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Rask WASM navigate '{navPath}' threw: {ex}");
                return string.Empty;
            }
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
        }
    }

    private async Task<byte[]> BuildPayloadCoalescingRerendersAsync(string? historyUrl, bool replace)
    {
        // First build emits any pending history navigation. If a dispose callback (or other
        // synchronous code reached during RenderAsLiveRoot) requested another render via
        // StateHasChanged, it set _pendingRenderInScope=true — rebuild so the dispatch's
        // returned payload carries the post-dispose state. Pass (null, false) on the rebuild:
        // the navigator's pending history entry was already consumed by the first build, and
        // re-passing historyUrl/replace would emit a duplicate history.pushState.
        _pendingRenderInScope = false;
        var payload = await BuildPayloadAsync(historyUrl, replace).ConfigureAwait(false);
        var budget = 2;
        while (_pendingRenderInScope && budget-- > 0)
        {
            _pendingRenderInScope = false;
            payload = await BuildPayloadAsync(null, false).ConfigureAwait(false);
        }

        return payload;
    }

    internal async Task<byte[]> BuildPayloadAsync(string? historyUrl, bool replace)
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

        // The App component owns the full page (Doctype + Html + Head + Body). Send the whole
        // document so the JS runtime can morph <head> too — title, stylesheet <link>s, and the
        // scoped-css <link> would otherwise stay frozen at whatever the static index.html shipped.
        var html = View.RenderAsLiveRoot(Services);

        var currentHash = ScopedCssRegistry.CurrentHash;
        string? cssText = null;
        if (currentHash != _lastCssHashSent)
        {
            cssText = ScopedCssRegistry.GetBundle().Css;
            _lastCssHashSent = currentHash;
        }

        Rask.Core.Routing.PendingDownload? download = null;
        if (Services.GetService<Rask.Core.Routing.IDownloadSink>() is { } sink && sink.TryConsume(out var pd))
        {
            download = pd;
        }

        // BuildPayloadUtf8WithRoot fuses InjectRootAttr + payload write on UTF-8 bytes,
        // emitting the whole document (head + body) so the JS-side morph against
        // document.documentElement can update head children. Writes into the pooled
        // _writeBuffer so the per-render ArrayBufferWriter allocation is gone; the
        // ToArray at the end is still needed today because the JS interop boundary
        // marshals byte[] (PR6 will swap that for ReadOnlyMemory<byte>).
        _writeBuffer.ResetWrittenCount();
        LivePayload.BuildPayloadUtf8WithRoot(_writeBuffer, html, "wasm", historyUrl, replace, cssText, null, download);
        return _writeBuffer.WrittenSpan.ToArray();
    }
}
