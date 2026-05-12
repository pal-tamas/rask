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

    // Plain instance bool, NOT AsyncLocal: the dispatch lock is owned by this session as a whole,
    // not by any one async chain. AsyncLocal would flow into Timer/Task captures created during a
    // render — those captured ExecutionContexts would later report InHandlerScope=true forever,
    // making background StateHasChanged calls (e.g. from a Timer in a user component) silently no-op.
    private string? _lastCssHashSent;
    private string? _lastAppliedPayload;

    public WasmLiveSession(Component view, IServiceProvider services)
    {
        View = view;
        Services = services;
        view.RenderHandle = this;

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
            // We're already inside a handler-scoped render; let the post-handler payload flush carry the new HTML.
            await Task.CompletedTask.ConfigureAwait(false);
            return;
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            var payload = await BuildPayloadAsync(null, false).ConfigureAwait(false);
            // Skip the JS interop call when nothing changed since the last apply. Catches
            // StateHasChanged calls that didn't ultimately modify any visible output.
            if (string.Equals(payload, _lastAppliedPayload, StringComparison.Ordinal))
            {
                return;
            }

            var extracted = PayloadExtractor.Extract(payload);
            JSInterop.ApplyRender(extracted.Html, extracted.CssHash, extracted.CssText, extracted.HistoryJson);
            _lastAppliedPayload = payload;
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
        }
    }

    Task IRenderHandle.RenderInScopeAsync()
    {
        // For WASM the in-handler renderer just refreshes the in-flight payload. Since DispatchAsync
        // returns one final payload after the handler completes, intermediate scope renders are no-ops here.
        return Task.CompletedTask;
    }

    private void OnUserChanged() => _ = RequestRenderAsync();

    public async Task<string> InitialRenderAsync()
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
                    if (!await View.TryInvokeHandlerAsync(handlerId, root).ConfigureAwait(false))
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

                    var payload = await BuildPayloadAsync(historyUrl, historyReplace).ConfigureAwait(false);
                    // Suppress the JS-side apply when nothing changed AND no navigation needs to flow.
                    // The JS host treats an empty string as "no-op." Always send when historyUrl is set.
                    if (historyUrl is null && string.Equals(payload, _lastAppliedPayload, StringComparison.Ordinal))
                    {
                        return string.Empty;
                    }

                    _lastAppliedPayload = payload;
                    return payload;
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
                var payload = await BuildPayloadAsync(fullUrl, replace).ConfigureAwait(false);
                _lastAppliedPayload = payload;
                return payload;
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

    internal async Task<string> BuildPayloadAsync(string? historyUrl, bool replace)
    {
        await Task.Yield();

        var routeState = Services.GetRequiredService<RouteState>();
        if (RouteResolver.TryResolve(routeState.Path, out var chain))
        {
            var user = Services.GetRequiredService<IUserProvider>().Current;
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
        var withRoot = LivePayload.InjectRootAttr(html, "wasm");

        var currentHash = ScopedCssRegistry.CurrentHash;
        string? cssText = null;
        if (currentHash != _lastCssHashSent)
        {
            cssText = ScopedCssRegistry.GetBundle().Css;
            _lastCssHashSent = currentHash;
        }

        return LivePayload.BuildPayload(withRoot, historyUrl, replace, cssText);
    }
}
