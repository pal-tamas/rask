using System.Buffers;
using System.Diagnostics;
using System.Globalization;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Microsoft.Net.Http.Headers;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Authorization;
using Rask.Core.Browser;
using Rask.Core.Components;
using Rask.Core.Diagnostics;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Core.ScopedAssets;
using Rask.Server.Authentication;
using Rask.Server.Diagnostics;
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

    // Hard cap on a single reassembled inbound WS frame. Client→server messages (hello / event
    // dispatch / jsResult / navigate / dotNetInvoke args) are small, and file uploads use the HTTP
    // endpoint — never the socket — so this is generous headroom. It bounds a per-socket memory DoS
    // where a client streams an unbounded fragmented frame the server would otherwise buffer whole
    // before JsonDocument.Parse. Seeded from RaskServerOptions.MaxInboundFrameBytes by AddRask; the
    // static is the DI-free hot-path source of truth (tests also set it directly).
    internal static int MaxInboundFrameBytes = 8 * 1024 * 1024;

    private static FileSystemWatcher? _sourceWatcher;
    private static long _lastSourceChangeTicks;

    internal static TimeSpan SessionGracePeriod = TimeSpan.FromSeconds(30);

    // Grace for a session that was minted by the GET shell but has not yet received a WS
    // `hello`. The runtime script connects within a second of page load, so a much shorter
    // window than the 30s reconnect grace is enough — and it stops a flood of GETs that never
    // open a socket (scanners, prefetchers, an unauthenticated DoS) from pinning a full DI
    // scope + component tree for 30s each. A real `hello` cancels this removal (LiveSessionStore
    // .Get) and any later disconnect re-arms the full SessionGracePeriod via DetachSocket.
    internal static TimeSpan UnconnectedSessionGracePeriod = TimeSpan.FromSeconds(10);

    // Maximum handler dispatches that may be queued (awaiting their turn in WS-arrival order)
    // before the receive loop trips the backpressure circuit-breaker and closes the socket.
    // Each queued dispatch holds a cloned JsonElement; without a bound, a client sending faster
    // than handlers drain — or a single hung handler stalling the chain head — would grow the
    // queue (and retained memory) without limit. 512 is far above any legitimate burst. 0 = off.
    internal static int MaxPendingHandlers = 512;

    // Maximum inbound WS messages per second on a single connection before the receive loop trips
    // and closes the socket. MaxInboundFrameBytes bounds one frame's SIZE and MaxPendingHandlers
    // bounds queued HANDLER dispatches, but neither bounds the rate of small NON-handler frames
    // (jsResult / navigate / dotNetInvoke / malformed), each of which still costs a JSON parse — so
    // a flood of them is a CPU DoS those caps miss. Counted over a sliding one-second window. A
    // realistic interaction peak (rapid typing with per-keystroke handlers, a 60 Hz scroll handler)
    // is well under 100/s, so 1000 is far above any legitimate burst. Mutable static so a test can
    // lower it. Seeded from RaskServerOptions.MaxInboundFramesPerSecond by AddRask; operators can also
    // tune ingress with a reverse-proxy rate limiter. 0 = off.
    internal static int MaxInboundFramesPerSecond = 1000;

    // Close a connected socket that sends no inbound frame for this long (the session survives for
    // reconnect under SessionGracePeriod). Seeded from RaskServerOptions.IdleSocketTimeout. Zero = off.
    internal static TimeSpan IdleSocketTimeout = TimeSpan.Zero;

    // Cancel a handler dispatch's CancellationToken after this long, so a cooperative handler that
    // observes it unwinds instead of pinning the render pipeline. Seeded from
    // RaskServerOptions.HandlerTimeout. Zero = off.
    internal static TimeSpan HandlerTimeout = TimeSpan.Zero;

    // Aggregate-bytes companion to MaxPendingHandlers: bounds the queued cloned-payload memory, not just
    // the queue count. Seeded from RaskServerOptions.MaxPendingHandlerBytes. 0 = off.
    internal static long MaxPendingHandlerBytes;

    private static readonly byte[] SessionUnknownPayload =
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "session", status = "unknown" }));

    // 50ms trailing-edge debounce for ScopedAssetRegistry.AssetChanged. A multi-file edit
    // (or a single hot-reload UpdateApplication burst that re-registers every component
    // back-to-back) generates N events; without coalescing each fires its own
    // RerenderAllAsync. The generation counter snapshot survives only if no newer event
    // arrived during the quiet window — the trailing change wins and we re-render once.
    private static long _assetChangeGen;

    // HTTP methods accepted by the per-component asset endpoint. GET serves the body;
    // HEAD returns the same headers with no body (handled by Results.Bytes internally).
    // POST/PUT/DELETE/PATCH not listed → ASP.NET returns 405 Method Not Allowed.
    private static readonly string[] _assetMethods = ["GET", "HEAD"];

    /// <summary>
    ///     Registers the Rask server-side live-rendering services (session store, routing,
    ///     authentication, file upload/download, <see cref="IJSRuntime" /> bridge, and the live
    ///     runtime script). Call this in <c>ConfigureServices</c>, then
    ///     <see cref="UseRask{TApp}(WebApplication, string, string)" />
    ///     in the pipeline. Authentication itself is configured on ASP.NET's own
    ///     <c>AddAuthentication</c>/<c>AddCookie</c>/<c>AddJwtBearer</c> — Rask has no auth options object.
    /// </summary>
    /// <param name="services">The service collection to add Rask services to.</param>
    /// <param name="configure">
    ///     Optional per-app live-runtime options shared by the Server and WASM runtimes (diff mode,
    ///     path base, session cap, scoped-asset preload). When omitted, framework defaults apply
    ///     (<see cref="Rask.Core.Live.LiveDiffMode.Auto" />, no path base, uncapped).
    /// </param>
    /// <param name="configureServer">
    ///     Optional server-host-only limits (<see cref="RaskServerOptions" />): the WebSocket
    ///     frame-size / frame-rate / pending-handler caps and the session grace periods. When omitted,
    ///     the framework's prior hardcoded defaults apply. Bind from configuration with
    ///     <c>AddRask(configureServer: o =&gt; config.GetSection("Rask").Bind(o))</c>.
    /// </param>
    /// <returns>The same <paramref name="services" /> instance, for chaining.</returns>
    public static IServiceCollection AddRask(this IServiceCollection services,
        Action<RaskLiveOptions>? configure = null,
        Action<RaskServerOptions>? configureServer = null)
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
        var maxSessions = 0;
        if (configure is not null)
        {
            var liveOptions = new RaskLiveOptions();
            configure(liveOptions);
            LiveOptions.DiffMode = liveOptions.DiffMode;
            // PathBase normalization happens on assignment to RaskLiveOptions,
            // and a second normalize on the static accessor is a cheap no-op.
            // UseRask<TApp>(pathBase: ...) can still override this if the user
            // prefers to set the prefix at endpoint-registration time.
            LiveOptions.PathBase = liveOptions.PathBase;
            LiveOptions.PreloadScopedAssets = liveOptions.PreloadScopedAssets;
            // Session cap is a per-store instance value (not a static) so concurrent
            // hosts/tests don't clobber each other through global state.
            maxSessions = liveOptions.MaxSessions;
        }

        // Seed the server-only WS / grace-period safety limits from RaskServerOptions. Defaults match
        // the statics, so an absent callback is a no-op. The statics are the DI-free hot-path source of
        // truth read by the WS receive loop; tests also set them directly.
        if (configureServer is not null)
        {
            var serverOptions = new RaskServerOptions();
            configureServer(serverOptions);
            serverOptions.Validate();
            MaxInboundFrameBytes = serverOptions.MaxInboundFrameBytes;
            MaxPendingHandlers = serverOptions.MaxPendingHandlers;
            MaxInboundFramesPerSecond = serverOptions.MaxInboundFramesPerSecond;
            SessionGracePeriod = serverOptions.SessionGracePeriod;
            UnconnectedSessionGracePeriod = serverOptions.UnconnectedSessionGracePeriod;
            IdleSocketTimeout = serverOptions.IdleSocketTimeout;
            HandlerTimeout = serverOptions.HandlerTimeout;
            MaxPendingHandlerBytes = serverOptions.MaxPendingHandlerBytes;
        }

        // Metrics singleton (Meter "Rask.Server"). TryAdd so a host can pre-register its own.
        services.TryAddSingleton<RaskMetrics>();
        services.AddSingleton(sp => new LiveSessionStore(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetService<IHostApplicationLifetime>(),
            sp.GetService<RaskMetrics>())
        { MaxSessions = maxSessions });
        services.AddSingleton<RaskLiveMarker>();
        services.AddScoped<RouteState>();
        services.AddScoped<Navigator>();
        services.AddScoped<IBrowserStorage, BrowserStorage>();
        services.AddScoped<IClipboard, Clipboard>();
        services.AddScoped<IGeolocation, Geolocation>();
        services.AddScoped<INavigatorInfo, NavigatorInfo>();
        services.AddScoped<INetworkInfo, NetworkInfo>();
        services.AddScoped<IMediaQuery, MediaQuery>();
        services.AddScoped<ISpeechSynthesis, SpeechSynthesis>();
        services.AddScoped<IScreenInfo, ScreenInfoReader>();
        services.AddScoped<ICookies, Cookies>();
        services.AddScoped<IPermissions, Permissions>();
        services.AddScoped<IVibration, Vibration>();
        services.AddScoped<IPageVisibility, PageVisibilityInfo>();
        // IShare is intentionally NOT registered on Server: navigator.share() requires transient user
        // activation, which is lost across the WebSocket round-trip. It is WASM-only (see WasmHostBuilder).
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

    /// <summary>
    ///     Maps the Rask live endpoints (root document, WebSocket dispatcher, scoped-asset, auth,
    ///     and upload/download routes) with <typeparamref name="TApp" /> as the root component, and
    ///     enables WebSockets. The root must render a complete shell
    ///     (<c>Doctype</c>/<c>Html</c>/<c>Head</c>/<c>Body</c>) — see RASK021.
    /// </summary>
    /// <typeparam name="TApp">The root <see cref="Component" /> rendered for every matched route.</typeparam>
    /// <param name="app">The web application to map endpoints on.</param>
    /// <param name="pattern">Catch-all route pattern Rask serves (default <c>/{**path}</c>).</param>
    /// <param name="pathBase">
    ///     Optional URL prefix so two Rask servers can share one origin behind a reverse proxy
    ///     (e.g. <c>/app1</c>). Overrides any path base set via <see cref="AddRask" />.
    /// </param>
    /// <returns>The same <paramref name="app" /> instance, for chaining.</returns>
    public static WebApplication UseRask<TApp>(
        this WebApplication app,
        string pattern = "/{**path}",
        string pathBase = "")
        where TApp : Component
    {
        // Route every framework diagnostic (Rask.Core + this host) into the application's logging
        // pipeline. No-ops when no ILoggerFactory is registered, leaving the stderr default in place.
        var loggerFactory = app.Services.GetService<ILoggerFactory>();
        RaskServerDiagnostics.Install(loggerFactory);

        var logger = loggerFactory?.CreateLogger("Rask");
        if (logger is not null)
            logger.LogInformation("Rask {Version} (Server) starting", RaskVersion.Current);
        else
            Console.WriteLine($"Rask {RaskVersion.Current} (Server) starting");

        app.UseWebSockets();
        ((IEndpointRouteBuilder)app).UseRask<TApp>(pattern, pathBase);
        return app;
    }

    /// <summary>
    ///     Endpoint-routing overload of <see cref="UseRask{TApp}(WebApplication, string, string)" />: maps the
    ///     Rask live endpoints onto an existing <see cref="IEndpointRouteBuilder" /> without touching the
    ///     middleware pipeline (the caller is responsible for <c>UseWebSockets()</c>).
    /// </summary>
    /// <typeparam name="TApp">The root <see cref="Component" /> rendered for every matched route.</typeparam>
    /// <param name="endpoints">The endpoint route builder to map Rask routes on.</param>
    /// <param name="pattern">Catch-all route pattern Rask serves (default <c>/{**path}</c>).</param>
    /// <param name="pathBase">Optional URL prefix; see the <see cref="WebApplication" /> overload.</param>
    /// <returns>The same <paramref name="endpoints" /> instance, for chaining.</returns>
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

            // Session-cap backstop (RaskLiveOptions.MaxSessions). TryCreate reserves a slot
            // atomically and returns null when over cap, rejecting BEFORE the component tree is
            // built so untrusted GET traffic can't exhaust memory (and a concurrent burst can't
            // race past the cap). Checked after the auth guard above so challenge/forbid
            // redirects (which create no session) still work.
            var session = store.TryCreate(sp =>
            {
                // Wrap the App in an implicit RootErrorBoundary so an uncaught render /
                // lifecycle / event-handler exception anywhere in the user's tree renders a
                // styled fallback page instead of an HTTP 500.
                var app = ActivatorUtilities.CreateInstance<TApp>(sp);
                return new RootErrorBoundary(app);
            });
            if (session is null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                httpContext.Response.Headers.RetryAfter = "5";
                await httpContext.Response.WriteAsync("Server is at session capacity; please retry shortly.")
                    .ConfigureAwait(false);
                return;
            }
            session.Services.GetRequiredService<SessionUserProvider>().Set(user);
            var routeState = session.Services.GetRequiredService<RouteState>();
            routeState.Path = path;
            routeState.Query = AdaptQuery(httpContext.Request.Query);
            // Render the GET shell and seed both baselines: the dedup baseline so a no-op
            // click after hello dedups against the HTML the browser already has (mirroring
            // WASM's InitialRenderAsync / `_lastAppliedHtml`), AND the diff-codec frame
            // baseline so the FIRST interactive WS render ships a diff instead of the whole
            // document. See LiveSession.RenderInitialRoot.
            var html = session.RenderInitialRoot();
            var content = LivePayload.InjectRootAttr(html, session.Id);
            httpContext.Response.ContentType = "text/html; charset=utf-8";
            // The shell embeds the session id (data-rask-root), which is the de-facto bearer
            // for the WS / upload / download endpoints. Forbid any shared-proxy / bfcache /
            // history caching so an authenticated user's session id can't be persisted and
            // replayed by another principal.
            httpContext.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";
            httpContext.Response.Headers.Pragma = "no-cache";
            await httpContext.Response.WriteAsync(content).ConfigureAwait(false);
            // Schedule cleanup in case no WS ever connects for this session.
            // Browsers / probes can hit the catch-all for resources that don't
            // need a live session (favicon.ico, robots.txt, scanner traffic) —
            // without this guard those sessions stay in the store forever.
            // Uses the SHORT unconnected grace: the runtime connects within ~1s, so a session
            // that never sends `hello` is almost certainly a probe / abandoned load and should
            // not pin a DI scope + tree for the full 30s reconnect window. A real hello cancels
            // this removal (LiveSessionStore.Get) and DetachSocket later re-arms the full grace.
            store.ScheduleRemoval(session.Id, UnconnectedSessionGracePeriod);
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

                // Cross-Site WebSocket Hijacking guard. The upgrade carries the user's auth
                // cookie, and CORS does not apply to WebSocket handshakes, so a page on another
                // origin could otherwise open an authenticated socket. Driving a session also
                // needs the unguessable sessionId (delivered in the page HTML, unreadable
                // cross-origin), but rejecting a mismatched Origin is the standard belt-and-
                // suspenders. Reuses the same host-only same-origin check as the redeem endpoint
                // (see IsSameOrigin) — non-browser clients omit Origin and are allowed.
                if (!IsSameOrigin(ctx.Request))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
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
            (HttpContext ctx, string sessionId, string token, LiveSessionStore sessionStore,
                    SessionDownloadStore downloads) =>
                HandleDownloadAsync(ctx, sessionId, token, sessionStore, downloads));

        var sessionStore = endpoints.ServiceProvider.GetRequiredService<LiveSessionStore>();
        SubscribeAssetChangedDebounced(sessionStore);
        TryEnableSourceWatcher(sessionStore);
    }

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
                    RaskDiagnostics.Report(
                        RaskLogLevel.Warning, "Rask.HotReload",
                        "Rask: debounced asset-change rerender failed", ex);
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
                        RaskDiagnostics.Report(
                            RaskLogLevel.Warning, "Rask.HotReload", "Rask: source-watch rerender failed", ex);
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
            RaskDiagnostics.Report(
                RaskLogLevel.Warning, "Rask.HotReload", "Rask: source watcher disabled", ex);
        }
    }

    // Parse an inbound WS frame, returning null on malformed JSON instead of throwing.
    // Keeps the receive loop alive across a single bad frame (see call site).
    private static JsonDocument? SafeParse(ReadOnlyMemory<byte> payload)
    {
        try
        {
            return JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Warning, "Rask.Live", "Rask Live: dropped malformed WS frame", ex);
            return null;
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
        var metrics = store.Metrics;
        LiveSession? session = null;
        var buffer = new byte[16 * 1024];
        var message = new ArrayBufferWriter<byte>(16 * 1024);

        // Sliding one-second window for the inbound frame-rate cap (MaxInboundFramesPerSecond).
        var rateWindowStartTick = Environment.TickCount64;
        var framesInWindow = 0;

        // One connection-scoped CTS for the idle-socket timeout (null when disabled). Armed across the
        // whole inbound message — first frame and every continuation fragment — and disarmed while the
        // message is dispatched, so a mid-fragment stall is reclaimed too and we don't allocate a CTS
        // per message. CancelAfter just reschedules the one internal timer.
        using var idleCts = IdleSocketTimeout > TimeSpan.Zero
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                ReadOnlyMemory<byte> payload;

                // Arm the idle timer for this whole message-receive cycle; the finally disarms it
                // before dispatch so a slow handler doesn't trip it. A fired timer (not a shutdown)
                // means the client went silent mid-stream — close the socket (the session survives for
                // reconnect under the grace period).
                idleCts?.CancelAfter(IdleSocketTimeout);
                var receiveToken = idleCts?.Token ?? ct;
                try
                {
                    result = await ws.ReceiveAsync(buffer, receiveToken);
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
                            // Same idle token covers continuation fragments, so a client that stalls
                            // mid-message is reclaimed too.
                            result = await ws.ReceiveAsync(buffer, receiveToken);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                return;
                            }

                            if (result.Count > 0)
                            {
                                message.Write(buffer.AsSpan(0, result.Count));
                            }

                            // Abort a socket that streams a frame past the cap rather than buffering it
                            // whole — bounds per-socket memory against a fragmented-frame DoS.
                            if (message.WrittenCount > MaxInboundFrameBytes)
                            {
                                metrics?.FrameRejected("size");
                                try { ws.Abort(); }
                                catch { }

                                return;
                            }
                        } while (!result.EndOfMessage);

                        payload = message.WrittenMemory;
                    }
                }
                catch (OperationCanceledException)
                    when (idleCts is not null && idleCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    metrics?.FrameRejected("idle");
                    await ClosePolicyViolationAsync(ws, "idle timeout").ConfigureAwait(false);
                    break;
                }
                finally
                {
                    idleCts?.CancelAfter(Timeout.InfiniteTimeSpan);
                }

                // Inbound frame-rate cap: count every completed receive over a sliding one-second
                // window and close the socket on a flood, before the per-frame parse below. Bounds
                // a small-frame CPU DoS that the size cap and handler backpressure don't cover. The
                // client reconnects (hello) against the intact session and resumes from current
                // state, same as the handler-backlog breaker.
                if (MaxInboundFramesPerSecond > 0)
                {
                    var nowTick = Environment.TickCount64;
                    if (nowTick - rateWindowStartTick >= 1000)
                    {
                        rateWindowStartTick = nowTick;
                        framesInWindow = 0;
                    }

                    if (++framesInWindow > MaxInboundFramesPerSecond)
                    {
                        metrics?.FrameRejected("rate");
                        await ClosePolicyViolationAsync(ws, "frame rate").ConfigureAwait(false);
                        break;
                    }
                }

                if (payload.Length == 0)
                {
                    continue;
                }

                // A malformed frame must not tear down the session. The receive loop's only
                // catches are OperationCanceledException / WebSocketException, so an unguarded
                // JsonException here would propagate to the finally and detach the socket —
                // letting one bad (buggy or adversarial) frame drop the whole live session.
                // Skip it and keep serving; the size cap above still bounds memory.
                using var doc = SafeParse(payload);
                if (doc is null)
                {
                    continue;
                }

                var root = doc.RootElement;

                // A valid-JSON but non-object root (a bare array / number / string) would make the
                // TryGetProperty calls below throw InvalidOperationException — another way one bad
                // frame could tear the session down. Skip it like a malformed frame.
                if (root.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
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

                // Backpressure circuit-breaker: bound both the number of dispatches queued on the
                // chain (MaxPendingHandlers) and their aggregate cloned-payload bytes
                // (MaxPendingHandlerBytes). When handlers drain slower than the client sends (a flood)
                // or the chain head is stuck (a hung handler), the queue — each entry holding a cloned
                // JsonElement — would grow without limit. Trip before cloning so we don't even allocate
                // the payload we'd have to drop; close the socket so the client reconnects (hello)
                // against the intact session and resumes from current state.
                var payloadBytes = (long)payload.Length;
                var pending = capturedSession.IncrementPendingHandlers();
                var pendingBytes = capturedSession.AddPendingHandlerBytes(payloadBytes);
                if ((MaxPendingHandlers > 0 && pending > MaxPendingHandlers)
                    || (MaxPendingHandlerBytes > 0 && pendingBytes > MaxPendingHandlerBytes))
                {
                    capturedSession.DecrementPendingHandlers();
                    capturedSession.SubtractPendingHandlerBytes(payloadBytes);
                    metrics?.FrameRejected("backlog");
                    await ClosePolicyViolationAsync(ws, "handler backlog").ConfigureAwait(false);
                    break;
                }

                // We clone the JSON element because the JsonDocument's backing buffer is disposed
                // at the bottom of the iteration.
                var capturedRoot = root.Clone();
                capturedSession.LastHandlerTask = ChainHandlerDispatchAsync(
                    capturedSession.LastHandlerTask,
                    capturedSession,
                    capturedHandlerId,
                    capturedRoot,
                    payloadBytes,
                    metrics,
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

    // Best-effort PolicyViolation close shared by the rate / backlog / idle breakers: the client
    // reconnects (hello) against the intact session. A 2 s deadline bounds a wedged close handshake.
    private static async Task ClosePolicyViolationAsync(WebSocket ws, string reason)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // Socket already faulted / closing — nothing to do.
        }
    }

    private static async Task ChainHandlerDispatchAsync(
        Task previous,
        LiveSession session,
        string handlerId,
        JsonElement root,
        long payloadBytes,
        RaskMetrics? metrics,
        CancellationToken ct)
    {
        try
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

            await DispatchHandlerAsync(session, handlerId, root, metrics, ct).ConfigureAwait(false);

            // Acknowledge the handler so the client's slow-link pending indicator can
            // resolve — crucially even when the render deduped and no frame was sent
            // (RenderAndSendAsync's HTML/byte dedup returns silently). Opt-in: only a
            // client that stamped a `seq` gets an ack, so seq-less clients keep the exact
            // prior frame contract. The ack rides SendOutOfBandAsync, which serialises on
            // the render lock, so it always lands after this handler's render frame and
            // before the next handler's (the chain awaits this whole task in order).
            if (root.TryGetProperty("seq", out var seqEl)
                && seqEl.ValueKind == JsonValueKind.Number
                && seqEl.TryGetInt64(out var seq))
            {
                await SendHandlerAckAsync(session, seq).ConfigureAwait(false);
            }
        }
        finally
        {
            // Pairs with the Increment/AddPendingHandlerBytes in the receive loop when this dispatch
            // was queued, so the backpressure count and byte total track the live chain depth.
            session.DecrementPendingHandlers();
            session.SubtractPendingHandlerBytes(payloadBytes);
        }
    }

    // Tiny out-of-band frame that closes the round-trip for a handler the client tagged
    // with a `seq`. Lets the browser's pending-action bar (rask.js) clear without the
    // server having to emit a render — the dedup path produces no frame, so the ack is
    // the client's only signal that a no-op click was processed. Best-effort: a missed
    // ack (socket closing, cancellation) is covered by the client's hard-timeout backstop.
    private static async Task SendHandlerAckAsync(LiveSession session, long seq)
    {
        try
        {
            var payload = Encoding.UTF8.GetBytes(
                "{\"type\":\"ack\",\"seq\":" + seq.ToString(CultureInfo.InvariantCulture) + "}");
            await session.SendOutOfBandAsync(payload).ConfigureAwait(false);
        }
        catch
        {
            // Swallow: the client re-syncs on the next ack or its hard-timeout backstop.
        }
    }

    private static async Task DispatchHandlerAsync(
        LiveSession session,
        string handlerId,
        JsonElement root,
        RaskMetrics? metrics,
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
        using var activity = RaskActivity.Source.StartActivity("rask.handler.dispatch");
        activity?.SetTag("rask.handler.id", handlerId);
        metrics?.HandlerDispatched();
        var dispatchStart = Stopwatch.GetTimestamp();

        // Handler timeout: cancel the dispatch's CancellationToken after HandlerTimeout (linked to
        // the socket so a close cancels it too). A handler that threads CancellationToken into its
        // async work unwinds cooperatively; one that ignores it can't be force-aborted (the timeout is
        // still logged + metered). Null when the timeout is disabled, so the default path allocates nothing.
        using var handlerCts = HandlerTimeout > TimeSpan.Zero
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;
        handlerCts?.CancelAfter(HandlerTimeout);
        var dispatchToken = handlerCts?.Token ?? default;
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
                    // Re-check route authorization for the *current* principal before running the
                    // handler. A user whose access was revoked mid-session (signed out elsewhere,
                    // role removed, cookie expired and re-resolved on a reconnect) must not get to
                    // fire a server-side handler on a page they can no longer view. When the guard
                    // no longer passes, skip the handler entirely and let EnforceAuthAndRenderAsync
                    // re-evaluate and ship the challenge/forbid redirect.
                    if (!await IsCurrentRouteAuthorizedAsync(session).ConfigureAwait(false))
                    {
                        await EnforceAuthAndRenderAsync(session, null, false).ConfigureAwait(false);
                    }
                    else if (await session.View.TryInvokeHandlerAsync(
                                 handlerId, root, session.Services, dispatchToken))
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
            catch (OperationCanceledException)
                when (handlerCts is not null && handlerCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // The handler timed out and observed CancellationToken, unwinding cleanly. The
                // session survives; record it rather than letting it look like a normal cancellation.
                metrics?.HandlerTimedOut();
                activity?.SetStatus(ActivityStatusCode.Error, "handler timed out");
                RaskDiagnostics.Report(
                    RaskLogLevel.Warning, "Rask.Live",
                    $"Rask Live handler '{handlerId}' cancelled after HandlerTimeout ({HandlerTimeout})");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                metrics?.HandlerFaulted();
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                RaskDiagnostics.Report(
                    RaskLogLevel.Error, "Rask.Live", $"Rask Live handler '{handlerId}' threw", ex);
            }
        }
        finally
        {
            session.InHandlerScope = false;
            session.Lock.Release();
            metrics?.RecordHandlerDuration(Stopwatch.GetElapsedTime(dispatchStart).TotalMilliseconds);
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
                w.WriteStringValue(
                    root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String
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
            RaskDiagnostics.Report(
                RaskLogLevel.Error, "Rask.Live", $"Rask jsResult dispatch for taskId={taskId} threw", ex);
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
            RaskDiagnostics.Report(
                RaskLogLevel.Error, "Rask.Live",
                $"Rask dotNetInvoke '{assemblyName}.{methodIdentifier}' threw", ex);
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
                RaskDiagnostics.Report(
                    RaskLogLevel.Error, "Rask.Live", $"Rask Live navigate '{navPath}' threw", ex);
            }
        }
        finally
        {
            session.InHandlerScope = false;
            session.Lock.Release();
        }
    }

    // Evaluates the route guard for the session's current route + principal. Returns true when the
    // route resolves and the guard allows it, or when no route resolves (nothing to gate — e.g. a
    // NotFound page); false when the guard would challenge/forbid. Used to gate handler dispatch.
    private static async Task<bool> IsCurrentRouteAuthorizedAsync(LiveSession session)
    {
        var routeState = session.Services.GetRequiredService<RouteState>();
        if (!RouteResolver.TryResolve(routeState.Path, out var chain))
        {
            return true;
        }

        var user = session.Services.GetRequiredService<SessionUserProvider>().Current;
        var result = await RouteAuthorizationGuard
            .EvaluateAsync(session.Services, chain, user)
            .ConfigureAwait(false);
        return result.Outcome == RouteAuthorizationOutcome.Allow;
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
                    // Client-side guard redirect targets. (The initial HTTP GET challenge already goes
                    // through the configured auth scheme's own LoginPath/AccessDeniedPath.)
                    var originalUrl = QueryString.Build(routeState.Path, routeState.Query);
                    var redirectPath = result.Outcome == RouteAuthorizationOutcome.Forbid
                        ? RouteAuthorizationGuard.ForbidPath
                        : RouteAuthorizationGuard.ChallengePath;
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
        // Defense-in-depth CSRF: the redeem ticket is a single-use, session-bound, 128-bit secret
        // delivered only over the authenticated same-origin WS frame, so classic cookie-CSRF can't
        // forge it. As belt-and-braces we still reject a cross-origin Origin/Referer — a forged POST
        // from another site is bounced before the (otherwise antiforgery-exempt) ticket is consulted.
        if (!IsSameOrigin(ctx.Request))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

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

    // True when the request carries no Origin/Referer (same-origin fetches may omit Origin — the
    // ticket secrecy covers those) or one whose host matches the request's own host.
    //
    // We compare host only — NOT scheme/port. Behind a TLS-terminating reverse proxy the browser's
    // Origin is https://app (:443) while request.Scheme/Host can be http (:80) unless ForwardedHeaders
    // is wired, so a scheme/port match would 403 a legitimate sign-in. Host is the CSRF-relevant axis
    // and the single-use session-bound ticket is the real authority (see RedeemAuthTicketAsync), so a
    // host-only check is the right belt-and-braces.
    private static bool IsSameOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            var referer = request.Headers.Referer.ToString();
            if (string.IsNullOrEmpty(referer))
            {
                return true;
            }

            origin = referer;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        // request.Host.Host is the host without the port (empty if the Host header is missing/malformed).
        var selfHost = request.Host.Host;
        return !string.IsNullOrEmpty(selfHost)
               && string.Equals(originUri.Host, selfHost, StringComparison.OrdinalIgnoreCase);
    }

    // Binds an id-addressed endpoint (upload / download) to the principal that owns the live
    // session. The {sessionId} in the URL is the only thing tying the request to a session, so a
    // leaked id must not let a *different* signed-in user drive a victim's session. An anonymous
    // session is matched by anyone — the unguessable sessionId is the only authority then (the same
    // posture the WS handshake takes); an authenticated session requires the request to carry the
    // same authenticated identity.
    private static bool SameSessionUser(ClaimsPrincipal request, ClaimsPrincipal owner)
    {
        if (owner.Identity?.IsAuthenticated != true)
        {
            return true;
        }

        if (request.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return string.Equals(UserKey(request), UserKey(owner), StringComparison.Ordinal);
    }

    private static string? UserKey(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.Identity?.Name;

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

    // Local-redirect only — shared with the client route-guard and WASM login flows via
    // Rask.Core's LocalUrl.Sanitize (single source of truth for the IsLocalUrl rule).
    internal static string SanitizeReturnUrl(string? returnUrl) => LocalUrl.Sanitize(returnUrl);

    // Reduce a client-supplied upload filename to a safe display leaf: drop any directory
    // components (both '/' and '\' separators, whatever the host OS), strip control characters
    // and NUL, cap the length, and fall back to a generic name when nothing usable remains. The
    // staged file is always written to a server-generated token path (never the name), so this is
    // defense-in-depth — the returned `name` is still attacker-controlled and hosts must
    // HTML-encode it before display (use Text / element children, never Raw).
    internal static string SanitizeUploadFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return "file";
        }

        // Take the segment after the last separator — handles "../../x", "C:\x", "a/b/c".
        var lastSeparator = fileName.AsSpan().LastIndexOfAny('/', '\\');
        var leaf = lastSeparator >= 0 ? fileName[(lastSeparator + 1)..] : fileName;

        var sb = new StringBuilder(leaf.Length);
        foreach (var ch in leaf)
        {
            if (!char.IsControl(ch))
            {
                sb.Append(ch);
            }

            if (sb.Length >= 255)
            {
                break;
            }
        }

        var cleaned = sb.ToString().Trim();
        return cleaned.Length == 0 || cleaned == "." || cleaned == ".." ? "file" : cleaned;
    }

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
                entityTag: new EntityTagHeaderValue(bytes.Value.Etag))
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

    private static void RollbackStaged(SessionUploadStore uploads, List<SessionUploadStore.Entry> staged)
    {
        foreach (var prior in staged)
        {
            uploads.Release(prior.SessionId, prior.Token);
        }
    }

    private static async Task HandleUploadAsync(
        HttpContext ctx,
        string sessionId,
        LiveSessionStore sessions,
        SessionUploadStore uploads,
        RaskUploadOptions options)
    {
        var session = sessions.Get(sessionId);
        if (session is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // The {sessionId} is the only credential on this multipart POST (DisableAntiforgery), so
        // guard it like the WS handshake: reject cross-origin posts and require the request's
        // authenticated user to match the session owner.
        if (!IsSameOrigin(ctx.Request)
            || !SameSessionUser(ctx.User, session.Services.GetRequiredService<SessionUserProvider>().Current))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
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
                RollbackStaged(uploads, staged);
                ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            long lastModified = 0;
            if (long.TryParse(form[$"{file.Name}__lastModified"].ToString(), out var lm))
            {
                lastModified = lm;
            }

            // StageAsync atomically enforces the cumulative per-session quota: a null return means this
            // file would push the session over MaxBytesPerSession (the temp file is already cleaned up).
            var entry = await uploads.StageAsync(
                sessionId,
                SanitizeUploadFileName(file.FileName),
                string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                file.Length,
                DateTimeOffset.FromUnixTimeMilliseconds(lastModified),
                async path =>
                {
                    await using var output = File.Create(path);
                    await using var input = file.OpenReadStream();
                    await input.CopyToAsync(output, ctx.RequestAborted).ConfigureAwait(false);
                },
                options.MaxBytesPerSession);

            if (entry is null)
            {
                RollbackStaged(uploads, staged);
                ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

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
        LiveSessionStore sessions,
        SessionDownloadStore downloads)
    {
        // A leaked download URL must not serve a victim's file to another principal: require the
        // session to still exist and the request to be same-origin and from the session owner
        // before consuming the one-shot entry.
        var session = sessions.Get(sessionId);
        if (session is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!IsSameOrigin(ctx.Request)
            || !SameSessionUser(ctx.User, session.Services.GetRequiredService<SessionUserProvider>().Current))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!downloads.TryTake(sessionId, token, out var entry) || entry is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        try
        {
            ctx.Response.ContentType = entry.ContentType;
            ctx.Response.Headers.CacheControl = "no-store";
            // The content-type is supplied by whoever staged the download (often echoed from a
            // client upload), so forbid MIME sniffing: paired with the attachment disposition below
            // it keeps a mislabelled file from being sniffed into an inline-rendered HTML/script.
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
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
