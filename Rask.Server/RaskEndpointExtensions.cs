using System.Buffers;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Authorization;
using Rask.Core.Components;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Core.ScopedAssets;
using Rask.Server.Authentication;
using Rask.Server.Files;
using Rask.Server.JSInterop;
using Components = Rask.Core.Components.Generated;
using IQueryCollection = Microsoft.AspNetCore.Http.IQueryCollection;
using QueryCollection = Rask.Core.Routing.QueryCollection;
using QueryString = Rask.Core.Routing.QueryString;

namespace Rask.Server;

public static class RaskEndpointExtensions
{
    private const string RuntimePath = "/rask/rask.js";
    private const string WebSocketPath = "/rask/ws";

    private static FileSystemWatcher? _sourceWatcher;
    private static long _lastSourceChangeTicks;

    internal static TimeSpan SessionGracePeriod = TimeSpan.FromSeconds(30);

    private static readonly byte[] SessionUnknownPayload =
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "session", status = "unknown" }));

    public static IServiceCollection AddRask(this IServiceCollection services,
        Action<Rask.Core.Live.RaskLiveOptions>? configure = null)
    {
        // Per-app live runtime options. The framework default for DiffMode is
        // LiveDiffMode.Auto (set on the static LiveOptions.DiffMode at class init),
        // so a fresh `AddRask()` ships the diff codec out of the box. Override:
        //     services.AddRask(o => o.DiffMode = LiveDiffMode.DisabledFull);
        //
        // Only write the static field when the caller provided a configure
        // callback — otherwise we'd clobber a value the host (or a test) already
        // set explicitly before AddRask was called. The static field starts at
        // Auto via its initializer, so the "no configure, no prior write" path
        // still lands on Auto without us re-writing it here.
        if (configure is not null)
        {
            var liveOptions = new Rask.Core.Live.RaskLiveOptions();
            configure(liveOptions);
            Rask.Core.Live.LiveOptions.DiffMode = liveOptions.DiffMode;
            // PathBase normalization happens on assignment to RaskLiveOptions,
            // and a second normalize on the static accessor is a cheap no-op.
            // UseRask<TApp>(pathBase: ...) can still override this if the user
            // prefers to set the prefix at endpoint-registration time.
            Rask.Core.Live.LiveOptions.PathBase = liveOptions.PathBase;
        }

        services.AddSingleton<LiveSessionStore>();
        services.AddSingleton<RaskLiveMarker>();
        services.AddScoped<RouteState>();
        services.AddScoped<Navigator>();
        services.AddScoped<AuthSignIn>();
        services.AddScoped<IAuthSignIn>(sp => sp.GetRequiredService<AuthSignIn>());
        services.AddSingleton<IAuthTicketStore, AuthTicketStore>();
        services.AddSingleton<IRaskRuntimeScript, ServerRuntimeScript>();
        services.AddSingleton<SessionUploadStore>();
        services.AddSingleton<SessionDownloadStore>();
        services.TryAddSingleton<RaskUploadOptions>();
        services.AddScoped<RaskSessionContext>();
        services.AddScoped<IBrowserFileBackend, ServerFileBackend>();
        services.AddScoped<IDownloadSink, ServerDownloadSink>();

        services.AddScoped<SessionUserProvider>();
        services.AddScoped<IUserProvider>(sp => sp.GetRequiredService<SessionUserProvider>());
        services.TryAddSingleton<RaskAuthorizationOptions>();
        services.AddAuthorization();

        // IJSRuntime compatibility. RaskJSRuntime + LiveSessionAccessor are scoped — one
        // pair per LiveSession DI scope. LiveSessionStore.Create sets accessor.Session
        // immediately after constructing the session, so any component that takes IJSRuntime
        // via ctor injection sees a runtime bound to the correct session. Self-binding via
        // GetRequiredService keeps a single instance shared between IJSRuntime resolution
        // and any direct RaskJSRuntime resolution (e.g. from the WS message handler when
        // dispatching dotNetInvoke / jsResult).
        services.AddScoped<LiveSessionAccessor>();
        services.AddScoped<RaskJSRuntime>();
        services.AddScoped<IJSRuntime>(sp => sp.GetRequiredService<RaskJSRuntime>());
        return services;
    }

    public static IServiceCollection AddRask(
        this IServiceCollection services,
        Action<RaskAuthorizationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new RaskAuthorizationOptions();
        configure(options);
        services.AddSingleton(options);
        return services.AddRask();
    }

    public static WebApplication UseRask<TApp>(
        this WebApplication app,
        string pattern = "/{**path}",
        string pathBase = "")
        where TApp : Component
    {
        app.UseWebSockets();
        ((IEndpointRouteBuilder)app).UseRask<TApp>(pattern, pathBase);
        return app;
    }

    public static IEndpointRouteBuilder UseRask<TApp>(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/{**path}",
        string pathBase = "")
        where TApp : Component
    {
        // Normalize once and stash on the static accessor so all downstream URL
        // emission (head asset links, runtime <script> src, download URLs) reads
        // the same prefix. A non-empty value also scopes every Map call below
        // under the prefix so two Rask servers can live side-by-side on one
        // origin behind a reverse proxy.
        var pathBaseNormalized = RaskPath.Normalize(pathBase);
        LiveOptions.PathBase = pathBaseNormalized;

        EnsureRuntimeMapped(endpoints, pathBaseNormalized);

        // Scope the catch-all SPA route under the prefix when set. The pattern
        // default ("/{**path}") is interpreted relative to the prefix root, so
        // a request to /sub/users/42 matches as Path.Value="/sub/users/42";
        // the handler strips the prefix before resolving against user routes
        // (which are registered as "/users/{id}").
        var scopedPattern = pathBaseNormalized.Length == 0
            ? pattern
            : pathBaseNormalized + (pattern.StartsWith('/') ? pattern : "/" + pattern);

        endpoints.MapGet(scopedPattern, async (HttpContext httpContext, LiveSessionStore store) =>
        {
            var path = StripPathBase(httpContext.Request.Path.Value ?? "/", pathBaseNormalized);
            var user = httpContext.User ?? new ClaimsPrincipal(new ClaimsIdentity());

            if (RouteResolver.TryResolve(path, out var chain))
            {
                var authResult = await RouteAuthorizationGuard
                    .EvaluateAsync(httpContext.RequestServices, chain, user)
                    .ConfigureAwait(false);

                switch (authResult.Outcome)
                {
                    case RouteAuthorizationOutcome.Challenge:
                        await ChallengeAsync(httpContext, authResult.AuthenticationScheme).ConfigureAwait(false);
                        return;
                    case RouteAuthorizationOutcome.Forbid:
                        await ForbidAsync(httpContext, authResult.AuthenticationScheme).ConfigureAwait(false);
                        return;
                }
            }

            var session = store.Create(sp =>
            {
                // Wrap the App in an implicit RootErrorBoundary so an uncaught render /
                // lifecycle / event-handler exception anywhere in the user's tree renders a
                // styled fallback page instead of an HTTP 500.
                var app = ActivatorUtilities.CreateInstance<TApp>(sp);
                return new RootErrorBoundary(app);
            });
            session.Services.GetRequiredService<SessionUserProvider>().Set(user);
            var routeState = session.Services.GetRequiredService<RouteState>();
            routeState.Path = path;
            routeState.Query = AdaptQuery(httpContext.Request.Query);
            var html = session.View.RenderAsLiveRoot(session.Services);
            // Seed the dedup baseline so the first post-hello WS frame can dedup against
            // the HTML the browser already has from this GET response (mirroring WASM's
            // InitialRenderAsync, which populates `_lastAppliedHtml` for the same reason).
            // Without this, a no-op click after hello would re-send the GET HTML verbatim.
            session.SeedInitialHtml(html);
            var content = LivePayload.InjectRootAttr(html, session.Id);
            httpContext.Response.ContentType = "text/html; charset=utf-8";
            await httpContext.Response.WriteAsync(content).ConfigureAwait(false);
            // Schedule cleanup in case no WS ever connects for this session.
            // Browsers / probes can hit the catch-all for resources that don't
            // need a live session (favicon.ico, robots.txt, scanner traffic) —
            // without this guard those sessions stay in the store forever.
            // When a legitimate WS hello arrives within the grace window,
            // LiveSessionStore.Get cancels the pending removal automatically.
            store.ScheduleRemoval(session.Id, SessionGracePeriod);
        });
        return endpoints;
    }

    // Strip the configured PathBase from a request path so RouteResolver and
    // RouteState see user-space paths (e.g. "/users/42") rather than mount-
    // scoped paths (e.g. "/sub/users/42"). Round-trips back into client-side
    // URLs via rask.js's prependBase() before they reach the History API.
    private static string StripPathBase(string path, string pathBase)
    {
        if (pathBase.Length == 0 || string.IsNullOrEmpty(path))
        {
            return string.IsNullOrEmpty(path) ? "/" : path;
        }

        if (path.Length == pathBase.Length && path.Equals(pathBase, StringComparison.Ordinal))
        {
            return "/";
        }

        if (path.Length > pathBase.Length
            && path[pathBase.Length] == '/'
            && path.StartsWith(pathBase, StringComparison.Ordinal))
        {
            return path[pathBase.Length..];
        }

        return path;
    }

    private static Task ChallengeAsync(HttpContext ctx, string? scheme) =>
        scheme is null ? ctx.ChallengeAsync() : ctx.ChallengeAsync(scheme);

    private static Task ForbidAsync(HttpContext ctx, string? scheme) =>
        scheme is null ? ctx.ForbidAsync() : ctx.ForbidAsync(scheme);

    private static QueryCollection AdaptQuery(IQueryCollection source)
    {
        if (source.Count == 0)
        {
            return QueryCollection.Empty;
        }

        var dict = new Dictionary<string, StringValues>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in source)
        {
            dict[kv.Key] = kv.Value;
        }

        return new QueryCollection(dict);
    }

    private static void EnsureRuntimeMapped(IEndpointRouteBuilder endpoints, string pathBase)
    {
        var marker = endpoints.ServiceProvider.GetRequiredService<RaskLiveMarker>();
        if (marker.RuntimeMapped)
        {
            return;
        }

        marker.RuntimeMapped = true;

        endpoints.Map(pathBase + WebSocketPath,
            async (HttpContext ctx, LiveSessionStore store, IHostApplicationLifetime lifetime) =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest)
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                var wsUser = ctx.User ?? new ClaimsPrincipal(new ClaimsIdentity());
                // permessage-deflate negotiation: the browser advertises the extension in the
                // upgrade request and we accept it. Render payloads are HTML-heavy and
                // compress ~10x. The "Dangerous" prefix in the property name warns about
                // CRIME/BREACH-style scenarios with attacker-controlled plaintext interleaved
                // with secrets on the same channel — not the situation here (per-session WS
                // frames carry the user's own rendered view, no cross-origin mixing).
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync(
                    new WebSocketAcceptContext { DangerousEnableCompression = true });
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    ctx.RequestAborted, lifetime.ApplicationStopping);
                await RunSocketLoop(ws, store, wsUser, linked.Token, lifetime.ApplicationStopping);
            });

        var script = LoadEmbeddedScript();
        endpoints.MapGet(pathBase + RuntimePath, () => Results.Text(script, "text/javascript; charset=utf-8"));

        // Per-component content-addressed asset endpoint. URL is immutable (hash is a
        // SHA-256 prefix of the bytes), so `Cache-Control: immutable` is safe and the
        // browser may reuse the cached entry for the configured `max-age` without
        // revalidating. Range/ETag/HEAD semantics come from Results.Bytes; OPTIONS is
        // handled by routing (405 falls through to ASP.NET's default for non-matching
        // methods). Marked `.AllowAnonymous()` so a host with a fallback authorization
        // policy still serves assets — content-addressed URLs carry no PII, and an unknown
        // hash returns 404 instead of leaking the registered set.
        endpoints.MapMethods(pathBase + "/_rask/a/{hash}.css", _assetMethods,
                static ctx => ServeAssetAsync(ctx, AssetKind.Css))
            .AllowAnonymous();
        endpoints.MapMethods(pathBase + "/_rask/a/{hash}.js", _assetMethods,
                static ctx => ServeAssetAsync(ctx, AssetKind.Js))
            .AllowAnonymous();

        endpoints.MapPost(pathBase + "/_rask/auth/redeem",
                (HttpContext ctx, IAuthTicketStore tickets) => RedeemAuthTicketAsync(ctx, tickets))
            .DisableAntiforgery();

        endpoints.MapPost(pathBase + "/_rask/upload/{sessionId}",
                (HttpContext ctx, string sessionId, LiveSessionStore sessionStore,
                        SessionUploadStore uploadStore, RaskUploadOptions options) =>
                    HandleUploadAsync(ctx, sessionId, sessionStore, uploadStore, options))
            .DisableAntiforgery();

        endpoints.MapGet(pathBase + "/_rask/download/{sessionId}/{token}",
            (HttpContext ctx, string sessionId, string token, SessionDownloadStore downloads) =>
                HandleDownloadAsync(ctx, sessionId, token, downloads));

        var sessionStore = endpoints.ServiceProvider.GetRequiredService<LiveSessionStore>();
        SubscribeAssetChangedDebounced(sessionStore);
        TryEnableSourceWatcher(sessionStore);
    }

    // 50ms trailing-edge debounce for ScopedAssetRegistry.AssetChanged. A multi-file edit
    // (or a single hot-reload UpdateApplication burst that re-registers every component
    // back-to-back) generates N events; without coalescing each fires its own
    // RerenderAllAsync. The generation counter snapshot survives only if no newer event
    // arrived during the quiet window — the trailing change wins and we re-render once.
    private static long _assetChangeGen;

    private static void SubscribeAssetChangedDebounced(LiveSessionStore sessionStore)
    {
        ScopedAssetRegistry.AssetChanged += (_, _) =>
        {
            var gen = Interlocked.Increment(ref _assetChangeGen);
            _ = Task.Run(async () =>
            {
                await Task.Delay(50).ConfigureAwait(false);
                if (Interlocked.Read(ref _assetChangeGen) != gen)
                {
                    return;
                }

                try
                {
                    await sessionStore.RerenderAllAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"Rask: debounced asset-change rerender failed: {ex.Message}");
                }
            });
        };
    }

    private static void TryEnableSourceWatcher(LiveSessionStore sessionStore)
    {
        if (_sourceWatcher is not null)
        {
            return;
        }

        if (Environment.GetEnvironmentVariable("DOTNET_WATCH") != "1")
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(Environment.CurrentDirectory)
            {
                IncludeSubdirectories = true,
                Filter = "*.cs",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            void Trigger()
            {
                var now = DateTime.UtcNow.Ticks;
                var prev = Interlocked.Exchange(ref _lastSourceChangeTicks, now);
                if (now - prev < TimeSpan.FromMilliseconds(250).Ticks)
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    await Task.Delay(150).ConfigureAwait(false);
                    try { await sessionStore.RerenderAllAsync().ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Rask: source-watch rerender failed: {ex.Message}");
                    }
                });
            }

            watcher.Changed += (_, _) => Trigger();
            watcher.Created += (_, _) => Trigger();
            watcher.Renamed += (_, _) => Trigger();
            _sourceWatcher = watcher;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Rask: source watcher disabled ({ex.Message})");
        }
    }

    private static async Task RunSocketLoop(WebSocket ws, LiveSessionStore store, ClaimsPrincipal wsUser,
        CancellationToken ct, CancellationToken stopping)
    {
        using var abortReg = stopping.Register(() =>
        {
            try { ws.Abort(); }
            catch { }
        });
        LiveSession? session = null;
        var buffer = new byte[16 * 1024];
        var message = new ArrayBufferWriter<byte>(16 * 1024);

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                ReadOnlyMemory<byte> payload;

                result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                if (result.EndOfMessage)
                {
                    // Hot path: single-fragment message. Parse JSON directly from the
                    // receive buffer slice — no UTF-8 string decode, no accumulator copy.
                    payload = buffer.AsMemory(0, result.Count);
                }
                else
                {
                    message.ResetWrittenCount();
                    if (result.Count > 0)
                    {
                        message.Write(buffer.AsSpan(0, result.Count));
                    }

                    do
                    {
                        result = await ws.ReceiveAsync(buffer, ct);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }

                        if (result.Count > 0)
                        {
                            message.Write(buffer.AsSpan(0, result.Count));
                        }
                    } while (!result.EndOfMessage);

                    payload = message.WrittenMemory;
                }

                if (payload.Length == 0)
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString()
                    : null;

                if (type == "hello")
                {
                    var sessionId = root.TryGetProperty("session", out var sid) && sid.ValueKind == JsonValueKind.String
                        ? sid.GetString()
                        : null;
                    if (sessionId is null)
                    {
                        continue;
                    }

                    session = store.Get(sessionId);
                    if (session is null)
                    {
                        await SendSessionUnknownAsync(ws, ct).ConfigureAwait(false);
                        return;
                    }

                    session.AttachSocket(ws, ct);
                    session.Services.GetRequiredService<SessionUserProvider>().Set(wsUser);
                    // Only emit a catch-up render when something asked to render during the
                    // GET→hello handoff window (or while detached across a reconnect). When
                    // no drop happened, the browser's HTML still reflects the session state
                    // and re-rendering would just re-fire OnRendered on every alive
                    // component for no visible change — that's what made Server's initial-
                    // mount hook count diverge from WASM's. FlushPendingRenderAsync is a
                    // no-op when nothing's pending.
                    await session.FlushPendingRenderAsync().ConfigureAwait(false);
                    continue;
                }

                if (session is null)
                {
                    continue;
                }

                if (session.SuppressEventsUntilReconnect)
                {
                    // Auth handoff in flight: a redeem fetch + WS reconnect are happening on
                    // the client. Drop everything except a future hello (handled above).
                    continue;
                }

                if (type == "navigate")
                {
                    await HandleNavigateAsync(session, root, ct);
                    continue;
                }

                if (type == "jsResult")
                {
                    // Round-trip reply for an IJSRuntime.InvokeAsync<T> call. The base
                    // JSRuntime class manages its own pending-task dictionary keyed by the
                    // taskId we passed out in jsInvokes; calling EndInvokeJS with the
                    // serialised [taskId, success, result|error] triple completes the
                    // awaiting ValueTask. No render needed.
                    HandleJsResult(session, root);
                    continue;
                }

                if (type == "dotNetInvoke")
                {
                    // JS-side DotNet.invokeMethodAsync calling into a [JSInvokable] method.
                    // Hand off to the public DotNetDispatcher; the runtime completes the
                    // call asynchronously and EndInvokeDotNet fires our SendOutOfBandAsync
                    // to deliver the result back to the client. No render needed.
                    HandleDotNetInvoke(session, root);
                    continue;
                }

                var handlerId = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    ? idEl.GetString()
                    : null;
                if (handlerId is null)
                {
                    continue;
                }

                // Dispatch the handler in WS-arrival order while keeping the receive
                // loop alive so async handlers can interleave with the jsResult /
                // dotNetInvoke frames they're awaiting (those paths run inline above —
                // never through DispatchHandlerAsync). The chain is rebuilt per
                // message: capture the prior tail, assign a new continuation that
                // awaits it before dispatching, and store the new continuation as
                // the next tail.
                //
                // Why not Task.Run + session.Lock.WaitAsync (the prior shape):
                // SemaphoreSlim is FIFO based on the order callers invoke WaitAsync,
                // not the order Task.Run was invoked. Under ThreadPool contention,
                // two messages spawned input→submit can race and acquire the lock
                // submit→input — letting submit handlers read a stale EditContext
                // that the preceding input handler hadn't applied yet. The async
                // chaining below pins start-of-dispatch order to WS-arrival order
                // without blocking the receive loop.
                //
                // We clone the JSON element because the JsonDocument's backing
                // buffer is disposed at the bottom of the iteration.
                var capturedSession = session;
                var capturedHandlerId = handlerId;
                var capturedRoot = root.Clone();
                capturedSession.LastHandlerTask = ChainHandlerDispatchAsync(
                    capturedSession.LastHandlerTask,
                    capturedSession,
                    capturedHandlerId,
                    capturedRoot,
                    ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            if (session is not null)
            {
                session.DetachSocket();
                store.ScheduleRemoval(session.Id, SessionGracePeriod);
            }

            if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", closeCts.Token); }
                catch { }
            }
        }
    }

    private static async Task ChainHandlerDispatchAsync(
        Task previous,
        LiveSession session,
        string handlerId,
        JsonElement root,
        CancellationToken ct)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch
        {
            // Previous handler's exceptions are already observed and logged inside
            // DispatchHandlerAsync — swallow here so a faulted predecessor doesn't
            // prevent this dispatch from running. The chain is for ordering only.
        }

        await DispatchHandlerAsync(session, handlerId, root, ct).ConfigureAwait(false);
    }

    private static async Task DispatchHandlerAsync(
        LiveSession session,
        string handlerId,
        JsonElement root,
        CancellationToken ct)
    {
        try
        {
            await session.Lock.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        session.InHandlerScope = true;
        try
        {
            var navigator = session.Services.GetRequiredService<Navigator>();
            var authSignIn = session.Services.GetRequiredService<AuthSignIn>();
            var ticketStore = session.Services.GetRequiredService<IAuthTicketStore>();
            try
            {
                using (navigator.EnterHandler())
                using (authSignIn.EnterHandler())
                {
                    if (await session.View.TryInvokeHandlerAsync(handlerId, root, session.Services))
                    {
                        string? historyUrl = null;
                        var historyReplace = false;
                        AuthInstruction? authInstruction = null;

                        if (authSignIn.TryConsume(out var pending))
                        {
                            var safeReturn = SanitizeReturnUrl(pending.ReturnUrl);
                            var ticketId = ticketStore.Issue(
                                pending.Action,
                                pending.Principal,
                                pending.Scheme,
                                session.Id);
                            authInstruction = new AuthInstruction(ticketId, safeReturn);

                            var routeState = session.Services.GetRequiredService<RouteState>();
                            var (path, query) = SplitUrl(safeReturn);
                            routeState.Path = path;
                            routeState.Query = query;
                            historyUrl = safeReturn;
                            historyReplace = true;
                        }

                        if (navigator.TryConsumeHistory(out var url, out var replace))
                        {
                            if (authInstruction is null)
                            {
                                historyUrl = url;
                                historyReplace = replace;
                            }
                        }

                        await EnforceAuthAndRenderAsync(
                                session, historyUrl, historyReplace, authInstruction)
                            .ConfigureAwait(false);

                        if (authInstruction is not null)
                        {
                            session.SuppressEventsUntilReconnect = true;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"Rask Live handler '{handlerId}' threw: {ex}");
            }
        }
        finally
        {
            session.InHandlerScope = false;
            session.Lock.Release();
        }
    }

    private static async Task SendSessionUnknownAsync(WebSocket ws, CancellationToken ct)
    {
        try
        {
            await ws.SendAsync(SessionUnknownPayload, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        catch { }
    }

    private static void HandleJsResult(LiveSession session, JsonElement root)
    {
        // Expected payload: { type: "jsResult", id: <long>, success: <bool>,
        //                     result?: <any-json>, error?: <string> }
        // Repackage as the [taskId, success, result|error] triple that
        // JSRuntime.EndInvokeJS(string) expects.
        if (!root.TryGetProperty("id", out var idEl)
            || idEl.ValueKind != JsonValueKind.Number
            || !idEl.TryGetInt64(out var taskId))
        {
            return;
        }

        var success = root.TryGetProperty("success", out var sEl) && sEl.ValueKind == JsonValueKind.True;

        var runtime = session.Services.GetService<RaskJSRuntime>();
        if (runtime is null)
        {
            return;
        }

        using var stream = new MemoryStream(128);
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartArray();
            w.WriteNumberValue(taskId);
            w.WriteBooleanValue(success);
            if (success)
            {
                if (root.TryGetProperty("result", out var resEl))
                {
                    resEl.WriteTo(w);
                }
                else
                {
                    w.WriteNullValue();
                }
            }
            else
            {
                w.WriteStringValue(root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String
                    ? errEl.GetString()
                    : "JS invocation failed");
            }

            w.WriteEndArray();
        }

        try
        {
            DotNetDispatcher.EndInvokeJS(runtime, Encoding.UTF8.GetString(stream.ToArray()));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Rask jsResult dispatch for taskId={taskId} threw: {ex}");
        }
    }

    private static void HandleDotNetInvoke(LiveSession session, JsonElement root)
    {
        // Expected payload: { type: "dotNetInvoke", callId: <string>,
        //                     assemblyName: <string>, methodIdentifier: <string>,
        //                     dotNetObjectId?: <long>, argsJson: <string> }
        var assemblyName = root.TryGetProperty("assemblyName", out var aEl) && aEl.ValueKind == JsonValueKind.String
            ? aEl.GetString()
            : null;
        var methodIdentifier = root.TryGetProperty("methodIdentifier", out var mEl)
                               && mEl.ValueKind == JsonValueKind.String
            ? mEl.GetString()
            : null;
        if (methodIdentifier is null)
        {
            return;
        }

        long dotNetObjectId = 0;
        if (root.TryGetProperty("dotNetObjectId", out var oEl) && oEl.ValueKind == JsonValueKind.Number)
        {
            oEl.TryGetInt64(out dotNetObjectId);
        }

        var callId = root.TryGetProperty("callId", out var cEl) && cEl.ValueKind == JsonValueKind.String
            ? cEl.GetString()
            : null;
        var argsJson = root.TryGetProperty("argsJson", out var argEl) && argEl.ValueKind == JsonValueKind.String
            ? argEl.GetString() ?? "[]"
            : "[]";

        var runtime = session.Services.GetService<RaskJSRuntime>();
        if (runtime is null)
        {
            return;
        }

        var invocationInfo = new DotNetInvocationInfo(assemblyName, methodIdentifier, dotNetObjectId, callId);
        try
        {
            DotNetDispatcher.BeginInvokeDotNet(runtime, invocationInfo, argsJson);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Rask dotNetInvoke '{assemblyName}.{methodIdentifier}' threw: {ex}");
        }
    }

    private static async Task HandleNavigateAsync(LiveSession session, JsonElement root, CancellationToken ct)
    {
        var navPath = root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
        if (string.IsNullOrEmpty(navPath))
        {
            return;
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

        await session.Lock.WaitAsync(ct);
        session.InHandlerScope = true;
        try
        {
            var routeState = session.Services.GetRequiredService<RouteState>();
            routeState.Path = navPath;
            routeState.Query = QueryString.Parse(navQueryString);

            try
            {
                await EnforceAuthAndRenderAsync(session, fullUrl, replace).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"Rask Live navigate '{navPath}' threw: {ex}");
            }
        }
        finally
        {
            session.InHandlerScope = false;
            session.Lock.Release();
        }
    }

    private static async Task EnforceAuthAndRenderAsync(
        LiveSession session,
        string? historyUrl,
        bool replace,
        AuthInstruction? auth = null)
    {
        // When emitting an auth instruction, skip route auth re-eval: the cookie hasn't
        // landed yet on this WS, so the SessionUserProvider still holds the pre-SignIn
        // principal. The post-reconnect render does the real check with the new identity.
        if (auth is null)
        {
            var routeState = session.Services.GetRequiredService<RouteState>();
            if (RouteResolver.TryResolve(routeState.Path, out var chain))
            {
                var user = session.Services.GetRequiredService<SessionUserProvider>().Current;
                var result = await RouteAuthorizationGuard
                    .EvaluateAsync(session.Services, chain, user)
                    .ConfigureAwait(false);
                if (result.Outcome != RouteAuthorizationOutcome.Allow)
                {
                    var options = session.Services.GetRequiredService<RaskAuthorizationOptions>();
                    var originalUrl = QueryString.Build(routeState.Path, routeState.Query);
                    var redirectPath = result.Outcome == RouteAuthorizationOutcome.Forbid
                        ? options.ForbidPath
                        : options.ChallengePath;
                    routeState.Path = redirectPath;
                    if (result.Outcome == RouteAuthorizationOutcome.Challenge)
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
        }

        await session.RenderAndSendCoalescingAsync(historyUrl, replace, auth).ConfigureAwait(false);
    }

    private static (string Path, QueryCollection Query) SplitUrl(string url)
    {
        var idx = url.IndexOf('?');
        if (idx < 0)
        {
            return (url, QueryCollection.Empty);
        }

        var path = url[..idx];
        var query = QueryString.Parse(url[idx..]);
        return (path, query);
    }

    private static async Task RedeemAuthTicketAsync(HttpContext ctx, IAuthTicketStore tickets)
    {
        string? ticketId = null;
        string? sessionId = null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("ticket", out var t) && t.ValueKind == JsonValueKind.String)
            {
                ticketId = t.GetString();
            }

            if (doc.RootElement.TryGetProperty("session", out var s) && s.ValueKind == JsonValueKind.String)
            {
                sessionId = s.GetString();
            }
        }
        catch (JsonException)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (string.IsNullOrEmpty(ticketId) || string.IsNullOrEmpty(sessionId))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!tickets.TryRedeem(ticketId, sessionId, out var ticket))
        {
            ctx.Response.StatusCode = StatusCodes.Status410Gone;
            return;
        }

        var scheme = await ResolveAuthSchemeAsync(ctx.RequestServices, ticket.Scheme).ConfigureAwait(false);
        if (ticket.Action == AuthAction.SignIn)
        {
            await ctx.SignInAsync(scheme, ticket.Principal!).ConfigureAwait(false);
        }
        else
        {
            await ctx.SignOutAsync(scheme).ConfigureAwait(false);
        }

        ctx.Response.StatusCode = StatusCodes.Status200OK;
    }

    private static async Task<string> ResolveAuthSchemeAsync(IServiceProvider services, string? explicitScheme)
    {
        if (!string.IsNullOrEmpty(explicitScheme))
        {
            return explicitScheme;
        }

        var provider = services.GetService<IAuthenticationSchemeProvider>();
        if (provider is not null)
        {
            var resolved = await provider.GetDefaultSignInSchemeAsync().ConfigureAwait(false);
            if (resolved is not null)
            {
                return resolved.Name;
            }
        }

        return CookieAuthenticationDefaults.AuthenticationScheme;
    }

    internal static string SanitizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            return "/";
        }

        if (!returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return "/";
        }

        return returnUrl;
    }

    // HTTP methods accepted by the per-component asset endpoint. GET serves the body;
    // HEAD returns the same headers with no body (handled by Results.Bytes internally).
    // POST/PUT/DELETE/PATCH not listed → ASP.NET returns 405 Method Not Allowed.
    private static readonly string[] _assetMethods = ["GET", "HEAD"];

    /// <summary>
    ///     Serves a single per-component scoped asset by its content hash. The hash and
    ///     extension are validated by routing pattern + a hex/length check here; on miss,
    ///     returns 404 (never exposes registered hashes). On hit, emits content-addressed
    ///     <c>Cache-Control: public, max-age=31536000, immutable</c>: the URL changes when
    ///     the bytes change, so the browser can hold the cached entry forever.
    /// </summary>
    internal static Task ServeAssetAsync(HttpContext ctx, AssetKind kind)
    {
        var hash = ctx.Request.RouteValues["hash"] as string;
        if (string.IsNullOrEmpty(hash) || !IsLowercaseHex(hash, ScopedAssetRegistry.HashHexLength))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        var bytes = ScopedAssetRegistry.GetByHash(hash, kind);
        if (bytes is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        // Set headers before invoking Results.Bytes so they are present on the response.
        ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";

        var contentType = kind == AssetKind.Css
            ? "text/css; charset=utf-8"
            : "text/javascript; charset=utf-8";

        // Results.Bytes wires ETag → If-None-Match (304), HEAD body suppression, and
        // Range request handling (206/416) when enableRangeProcessing is true.
        return Results.Bytes(
                bytes.Value.Utf8.ToArray(),
                contentType,
                enableRangeProcessing: true,
                entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue(bytes.Value.Etag))
            .ExecuteAsync(ctx);
    }

    private static bool IsLowercaseHex(string s, int expectedLength)
    {
        if (s.Length != expectedLength)
        {
            return false;
        }

        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static string LoadEmbeddedScript()
    {
        var asm = typeof(RaskEndpointExtensions).Assembly;
        var name = asm.GetManifestResourceNames()
                       .FirstOrDefault(n => n.EndsWith("rask.js", StringComparison.Ordinal))
                   ?? throw new InvalidOperationException("rask.js embedded resource not found.");
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static async Task HandleUploadAsync(
        HttpContext ctx,
        string sessionId,
        LiveSessionStore sessions,
        SessionUploadStore uploads,
        RaskUploadOptions options)
    {
        if (sessions.Get(sessionId) is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!ctx.Request.HasFormContentType)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted).ConfigureAwait(false);
        if (form.Files.Count == 0)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (form.Files.Count > options.MaxFilesPerRequest)
        {
            ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var staged = new List<SessionUploadStore.Entry>(form.Files.Count);
        foreach (var file in form.Files)
        {
            if (file.Length > options.MaxFileSize)
            {
                foreach (var prior in staged)
                {
                    uploads.Release(prior.SessionId, prior.Token);
                }

                ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            long lastModified = 0;
            if (long.TryParse(form[$"{file.Name}__lastModified"].ToString(), out var lm))
            {
                lastModified = lm;
            }

            var entry = uploads.Stage(
                sessionId,
                file.FileName,
                string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                file.Length,
                DateTimeOffset.FromUnixTimeMilliseconds(lastModified),
                async path =>
                {
                    await using var output = File.Create(path);
                    await using var input = file.OpenReadStream();
                    await input.CopyToAsync(output, ctx.RequestAborted).ConfigureAwait(false);
                });
            staged.Add(entry);
        }

        ctx.Response.ContentType = "application/json; charset=utf-8";
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("files");
            foreach (var entry in staged)
            {
                writer.WriteStartObject();
                writer.WriteString("token", entry.Token);
                writer.WriteString("name", entry.Name);
                writer.WriteNumber("size", entry.Size);
                writer.WriteString("type", entry.ContentType);
                writer.WriteNumber("lastModified", entry.LastModified.ToUnixTimeMilliseconds());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        await ctx.Response.Body.WriteAsync(ms.ToArray(), ctx.RequestAborted).ConfigureAwait(false);
    }

    private static async Task HandleDownloadAsync(
        HttpContext ctx,
        string sessionId,
        string token,
        SessionDownloadStore downloads)
    {
        if (!downloads.TryTake(sessionId, token, out var entry) || entry is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        try
        {
            ctx.Response.ContentType = entry.ContentType;
            ctx.Response.Headers.CacheControl = "no-store";
            var disposition = $"attachment; filename=\"{Uri.EscapeDataString(entry.Filename)}\"";
            ctx.Response.Headers["Content-Disposition"] = disposition;

            if (entry.Bytes is { } bytes)
            {
                ctx.Response.ContentLength = bytes.Length;
                await ctx.Response.Body.WriteAsync(bytes, ctx.RequestAborted).ConfigureAwait(false);
                return;
            }

            if (entry.TempPath is { } tempPath && File.Exists(tempPath))
            {
                var info = new FileInfo(tempPath);
                ctx.Response.ContentLength = info.Length;
                await using var fs = File.OpenRead(tempPath);
                await fs.CopyToAsync(ctx.Response.Body, ctx.RequestAborted).ConfigureAwait(false);
            }
        }
        finally
        {
            downloads.Release(entry);
        }
    }

    private sealed class ServerRuntimeScript : IRaskRuntimeScript
    {
        public Component Render() => Components.Script(LiveOptions.PathBase + RuntimePath);
    }

    internal sealed class RaskLiveMarker
    {
        public bool RuntimeMapped;
    }
}
