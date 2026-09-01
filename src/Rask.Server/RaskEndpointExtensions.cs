using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.WebSockets;
using System.Reflection.Metadata;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
using Rask.Core.Globalization;
using Rask.Core.HotReload;
using Rask.Core.Http;
using Rask.Core.Live;
using Rask.Core.Messaging;
using Rask.Core.Rendering;
using Rask.Core.Routing;
using Rask.Core.ScopedAssets;
using Rask.Hosting.Shared;
using Rask.Html.Components;
using Rask.Server.Authentication;
using Rask.Server.Diagnostics;
using Rask.Server.Files;
using Rask.Server.Http;
using Rask.Server.JSInterop;
using IQueryCollection = Microsoft.AspNetCore.Http.IQueryCollection;
using QueryCollection = Rask.Core.Routing.QueryCollection;
using QueryString = Rask.Core.Routing.QueryString;

namespace Rask.Server;

/// <summary>
///     The endpoints that make an ASP.NET Core app a Rask app: the page routes, the live WebSocket the
///     diff runtime talks over, and the client runtime script. Wired up by <c>UseRask&lt;TApp&gt;()</c>.
/// </summary>
[global::Rask.Core.RaskMarkup]
public static partial class RaskEndpointExtensions
{
    private const string RuntimePath = "/rask/rask.js";
    private const string WebSocketPath = "/rask/ws";

    // PWA endpoints, mapped only when AddRaskPwa registered a RaskPwaState. The manifest is under /rask/;
    // the service worker is served at the app root (NOT under /rask/) so its default control scope covers
    // the whole app — a SW under /rask/ could only intercept /rask/* requests.
    internal const string ManifestPath = "/rask/manifest.webmanifest";
    internal const string ServiceWorkerPath = "/rask-sw.js";

    // The WS receive-loop / session-lifecycle safety limits (frame size &amp; rate caps, pending-handler
    // count &amp; bytes, handler + idle-socket timeouts, reconnect grace periods) live on a per-host
    // RaskServerLimits singleton — resolved once per connection, read on the hot path via instance
    // fields — instead of process-global statics. See RaskServerLimits / RaskServerOptions.
    // A fixed, content-free payload — written as a literal rather than JsonSerializer.Serialize(anonymous)
    // so it needs no reflection-based serialization. Under NativeAOT (reflection JSON disabled) the
    // serializer call threw at static-init and crashed UseRask before the host could start.
    private static readonly byte[] SessionUnknownPayload =
        Encoding.UTF8.GetBytes("""{"type":"session","status":"unknown"}""");


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
    /// <param name="configureCulture">
    ///     The languages this app ships, and how a visitor's is chosen. Leaving it unset — the default —
    ///     keeps culture support off, and the rendered document is byte-for-byte what it was before:
    ///     <c>&lt;html lang="en"&gt;</c> with no <c>dir</c>.
    ///     <code>
    ///     services.AddRask(configureCulture: c =&gt;
    ///     {
    ///         c.SupportedCultures.Add("en");   // the first entry is the default
    ///         c.SupportedCultures.Add("hu");
    ///     });
    ///     </code>
    /// </param>
    /// <returns>The same <paramref name="services" /> instance, for chaining.</returns>
    public static IServiceCollection AddRask(this IServiceCollection services,
        Action<RaskLiveOptions>? configure = null,
        Action<RaskServerOptions>? configureServer = null,
        Action<RaskCultureOptions>? configureCulture = null)
    {
        // Per-app live runtime options. The framework default for DiffMode is LiveDiffMode.Auto, so a
        // fresh `AddRask()` ships the diff codec out of the box. Override:
        //     services.AddRask(o => o.DiffMode = LiveDiffMode.DisabledFull);
        //
        // DiffMode is a per-host value carried on the LiveSessionStore (and handed to each LiveSession),
        // NOT a process-global static — so two hosts in one process, and parallel tests, each render in
        // their own mode instead of racing shared state. PathBase / MinifyScopedAssets stay on the
        // static LiveOptions because they back the process-wide content-addressed asset registries.
        var maxSessions = 0;
        var diffMode = LiveDiffMode.Auto;
        if (configure is not null)
        {
            var liveOptions = new RaskLiveOptions();
            configure(liveOptions);
            diffMode = liveOptions.DiffMode;
            // PathBase normalization happens on assignment to RaskLiveOptions,
            // and a second normalize on the static accessor is a cheap no-op.
            // UseRask<TApp>(pathBase: ...) can still override this if the user
            // prefers to set the prefix at endpoint-registration time.
            LiveOptions.PathBase = liveOptions.PathBase;
            // Only honour an explicit true/false here; null (the default) is left for UseRask to resolve
            // from the host environment (minify outside Development). Writing null would clobber a value a
            // test set directly, so guard it.
            if (liveOptions.MinifyScopedAssets is { } minify)
            {
                LiveOptions.MinifyScopedAssets = minify;
            }

            // Session cap is a per-store instance value (not a static) so concurrent
            // hosts/tests don't clobber each other through global state.
            maxSessions = liveOptions.MaxSessions;
        }

        // Seed the server-only WS / grace-period safety limits from RaskServerOptions into a per-host
        // RaskServerLimits singleton. An absent callback registers the framework defaults (which match
        // RaskServerOptions' defaults). The singleton — not a process-global static — is the hot-path
        // source of truth: the WS endpoint resolves it once per connection so concurrent hosts each
        // carry their own limits. Validate only when configured (defaults are in range).
        var serverOptions = new RaskServerOptions();
        if (configureServer is not null)
        {
            configureServer(serverOptions);
            serverOptions.Validate();
        }

        services.AddSingleton(RaskServerLimits.From(serverOptions));

        // Seals the record a client carries from one session to the next. Live only when the host has Data
        // Protection (WebApplication.CreateBuilder does; a hand-rolled host might not) AND resume is on —
        // absent either, a reconnect to an unknown session falls back to the reload it did before rather
        // than failing the host at startup. Note this is what makes a persisted key ring load-bearing: with
        // the default per-container ring, a record sealed before a redeploy cannot be opened after it —
        // which is what RaskDataProtectionSetup below is for.
        var resumeEnabled = serverOptions.SessionResume;
        var resumeLifetime = serverOptions.ResumeTokenLifetime;

        // Ask for Data Protection rather than assuming it. WebApplication.CreateBuilder does NOT register
        // it — it arrives only because something else pulled it in (antiforgery, cookie auth, session),
        // which an app with none of those does not have. The call is idempotent and additive, exactly as
        // ASP.NET's own components use it: an app that configures the key ring itself is configuring this
        // same instance.
        //
        // UNCONDITIONAL, and it has to come FIRST. AddDataProtection registers ASP.NET's own
        // DataProtectionOptionsSetup, which writes ApplicationDiscriminator without checking whether
        // anything already set it. Left until later — by a resume-less app whose AddAuthentication pulls
        // Data Protection in below this line — that setup lands after ours and quietly reverts the
        // discriminator to the content-root default, so the ring persists but two containers still derive
        // different keys from it. Registering here puts ASP.NET's setup ahead of ours, and its TryAdd makes
        // every later AddDataProtection a no-op for the ordering.
        services.AddDataProtection();

        // Put the key ring somewhere that outlives the container, when the host has such a place. Registered
        // unconditionally and AFTER AddDataProtection: it is inert on a host that never protects anything,
        // it overrides ASP.NET's discovered default, and an app configuring its own ring after AddRask still
        // wins, because options setups run in registration order. See RaskDataProtectionSetup for why an
        // ephemeral ring signs every user out on redeploy without logging anything.
        //
        // Through its own factory rather than by constructor activation: the host services it reads are
        // OPTIONAL. A container that is not a host — a test fixture, a benchmark harness — has no
        // IConfiguration, and activating this by constructor there threw the first time anything
        // materialised the options, a long way from the AddRask that caused it (#922).
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<KeyManagementOptions>, RaskDataProtectionSetup>(
                RaskDataProtectionSetup.Create));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<DataProtectionOptions>, RaskDataProtectionSetup>(
                RaskDataProtectionSetup.Create));

        // Stop the hosted services CONCURRENTLY, inside a budget that fits under the deploy's SIGKILL.
        // Sequentially — .NET's default — each pillar's shutdown grace sums to 30s against a window that
        // closes at 20s, so whichever one stops last is killed mid-write, decided by the order of the
        // AddRaskX calls above. See RaskShutdownDefaults; override by configuring HostOptions after AddRask.
        //
        // Rask.Wasm.Hosting and Rask.Spa.Hosting register the same pair from the same source-linked types,
        // because their hosts face the same two failures. A wasm-hosted app with the dashboard on calls
        // both and gets one setup per assembly — they compute identical values, so it is idempotent.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<HostOptions>, RaskShutdownDefaults>());

        services.TryAddSingleton(sp =>
        {
            var provider = resumeEnabled ? sp.GetService<IDataProtectionProvider>() : null;
            return new SessionResumeSupport(
                provider is null ? null : new SessionHandoffProtector(provider, resumeLifetime));
        });

        // Metrics singleton (Meter "Rask.Server"). TryAdd so a host can pre-register its own.
        services.TryAddSingleton<RaskMetrics>();
        // Per-host shutdown state. Pure state with no dependencies, so the store can read it without a
        // construction cycle; RaskDrainService drives it.
        services.AddSingleton<RaskDrainCoordinator>();
        services.AddSingleton(sp => new LiveSessionStore(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetService<IHostApplicationLifetime>(),
            sp.GetService<RaskMetrics>())
        {
            MaxSessions = maxSessions,
            DiffMode = diffMode,
            // Assigned rather than passed: LiveSessionStore's constructor is public and
            // RaskDrainCoordinator is internal, and every directly-constructed test store keeps working
            // (it simply never drains).
            Drain = sp.GetRequiredService<RaskDrainCoordinator>(),
        });
        // Graceful shutdown for the live sessions: announce, settle in-flight handlers, close each socket
        // with a real handshake, dispose awaited. Registered unconditionally — a drain is not an opt-in.
        services.AddHostedService<RaskDrainService>();
        services.AddSingleton<RaskLiveMarker>();
        services.AddScoped<RouteState>();
        services.AddScoped<Navigator>();
        services.AddScoped<ServerPageResponse>();
        services.AddScoped<IPageResponse>(sp => sp.GetRequiredService<ServerPageResponse>());
        // The declared state bag (docs/lifecycle.md). Scoped = one per live session, like RouteState. A
        // session's component tree can't be serialized, so it can't be moved or saved; what an app names
        // here is what survives the session being rebuilt somewhere else.
        services.AddScoped<PersistentState>();
        services.AddScoped<IPersistentState>(sp => sp.GetRequiredService<PersistentState>());
        // Transient user messages / toasts (a flash-message pattern). Scoped = one queue per session, so a
        // message queued before a client-side NavigateTo survives the navigation and shows once on arrival.
        services.AddScoped<IToaster, Toaster>();

        // Scoped: a DI scope on the server IS a live session, and so a visitor. Registered even when
        // the app configured nothing, because IRaskCulture is a host contract; without a configured
        // culture this is inert. Negotiating one from the request arrives in a later change.
        services.AddRaskCulture(configureCulture, ServiceLifetime.Scoped);
        // Typed browser/device API wrappers — the transport-agnostic Core set, Scoped (one per WebSocket
        // session). Registered via the shared helper (RaskBrowserApis) so the interface → impl list lives in
        // one place instead of being duplicated across the Server and WASM hosts. TryAdd inside the helper
        // lets an app pre-register a better implementation and win. The PWA members (IWebPush, INotifications,
        // IBadge, IWakeLock) are included; their JS helpers ship in the Server client only under AddRaskPwa.
        // The remaining browser APIs are intentionally NOT registered on Server: they need transient user
        // activation, a live document/handle, or the installed-PWA instance the WebSocket round-trip loses,
        // so they are provided only by the WASM host (IShare and the rest of the WASM-only set — see
        // RaskWasmBrowserApis). Server can still reach the
        // activation-gated APIs declaratively via GestureTrigger — see docs/browser-capabilities.md.
        services.AddCoreBrowserApis(ServiceLifetime.Scoped);
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
    ///     enables WebSockets. The root renders into <c>&lt;body&gt;</c> — Rask composes the document
    ///     around it (see <c>Component.Shell</c> / <c>HtmlLang</c> / <c>BodyClass</c>); a root that renders
    ///     the shell itself is RASK021.
    /// </summary>
    /// <typeparam name="TApp">The root <see cref="Component" /> rendered for every matched route.</typeparam>
    /// <param name="app">The web application to map endpoints on.</param>
    /// <param name="pattern">Catch-all route pattern Rask serves (default <c>/{**path}</c>).</param>
    /// <param name="pathBase">
    ///     Optional URL prefix so two Rask servers can share one origin behind a reverse proxy
    ///     (e.g. <c>/app1</c>). Overrides any path base set via <see cref="AddRask" />.
    /// </param>
    /// <returns>The same <paramref name="app" /> instance, for chaining.</returns>
    public static WebApplication UseRask<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
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

        WarnOnTightShutdownLadder(app.Services, logger);

        // Resolve the scoped-CSS minification default from the host environment unless an explicit
        // true/false was already set (via AddRask or directly): minify outside Development, and keep it
        // readable + hot-reloadable in Development.
        LiveOptions.MinifyScopedAssets ??= !app.Environment.IsDevelopment();

        // Same idea, and the reason it is here rather than in Core: the host knows the answer, and every
        // way of selecting Development that ISN'T an environment variable — --environment, appsettings,
        // an IDE profile — used to give you the production error page while developing (#605).
        LiveOptions.IsDevelopment ??= app.Environment.IsDevelopment();

        app.UseWebSockets();
        ((IEndpointRouteBuilder)app).UseRask<TApp>(pattern, pathBase);
        return app;
    }

    /// <summary>
    ///     <see cref="AddRask(IServiceCollection, Action{RaskLiveOptions}, Action{RaskServerOptions}, Action{RaskCultureOptions})" />
    ///     under a name only this package defines — for an app that references <b>both</b> hosts.
    ///     <para>
    ///         <c>Rask.Wasm.Hosting</c> declares an <c>AddRask(this IServiceCollection)</c> as well, and
    ///         with both namespaces imported a bare <c>AddRask()</c> is <em>not</em> reported as
    ///         ambiguous: that overload takes no optional parameters and this one takes two, so C#'s
    ///         "fewer defaulted arguments" tie-break silently selects the other package's. The app
    ///         compiles, starts with no live runtime registered, and fails on the first request with a
    ///         missing-service error naming an internal type. Spelling the host out avoids relying on a
    ///         tie-break to express intent.
    ///     </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Per-app live runtime options; see the <c>AddRask</c> it forwards to.</param>
    /// <param name="configureServer">Server-host-only limits; see the <c>AddRask</c> it forwards to.</param>
    public static IServiceCollection AddRaskServer(
        this IServiceCollection services,
        Action<RaskLiveOptions>? configure = null,
        Action<RaskServerOptions>? configureServer = null) =>
        services.AddRask(configure, configureServer);

    /// <summary>
    ///     <see cref="UseRask{TApp}(WebApplication, string, string)" /> under a name only this package
    ///     defines — for an app that references both hosts.
    ///     <para>
    ///         The two <c>UseRask&lt;TApp&gt;</c> overloads differ only in what their second string
    ///         means: a route <em>pattern</em> here, a bundle <em>path</em> in <c>Rask.Wasm.Hosting</c>.
    ///         A wasm-hosted app that mounts the operator dashboard calls both, and at the call site
    ///         nothing distinguishes them.
    ///     </para>
    /// </summary>
    /// <typeparam name="TApp">The root <see cref="Component" /> rendered for every matched route.</typeparam>
    /// <param name="app">The web application to map endpoints on.</param>
    /// <param name="pattern">Catch-all route pattern Rask serves (default <c>/{**path}</c>).</param>
    /// <param name="pathBase">Optional URL prefix; see the <c>UseRask</c> it forwards to.</param>
    public static WebApplication UseRaskServer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        this WebApplication app,
        string pattern = "/{**path}",
        string pathBase = "")
        where TApp : Component =>
        app.UseRask<TApp>(pattern, pathBase);

    /// <summary>
    ///     Warns when the shutdown drain cannot fit inside the host's own shutdown budget. A warning and
    ///     not a throw: <c>Validate</c> throws for values that are <em>invalid</em>, whereas a tight
    ///     ladder is merely suboptimal, and refusing to start a production app over it would be a far
    ///     worse failure than the degraded shutdown it is warning about.
    /// </summary>
    private static void WarnOnTightShutdownLadder(IServiceProvider services, ILogger? logger)
    {
        if (logger is null || services.GetService<RaskServerLimits>() is not { } limits)
        {
            return;
        }

        var drain = limits.ShutdownDrainTimeout;
        if (drain <= TimeSpan.Zero || services.GetService<IOptions<HostOptions>>()?.Value is not { } host)
        {
            return;
        }

        if (host.ShutdownTimeout > drain)
        {
            return;
        }

        logger.LogWarning(
            "Rask: ShutdownDrainTimeout ({Drain}) is not below HostOptions.ShutdownTimeout ({Shutdown}), so live "
            + "sessions will be aborted at shutdown (the browser sees an abnormal 1006 close and reports a timed-out "
            + "session) instead of closed cleanly. Raise ShutdownTimeout or lower ShutdownDrainTimeout. Note that "
            + "HostOptions.ServicesStopConcurrently is false by default, so other hosted services spend from the same "
            + "budget before Rask's drain is even entered.",
            drain, host.ShutdownTimeout);
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
    public static IEndpointRouteBuilder UseRask<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
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

        // How a session's tree is built, captured once here because two call sites need it: the GET that
        // mints a session, and the WebSocket that rebuilds one from a resume record. Wrapping the App in an
        // implicit RootErrorBoundary means an uncaught render / lifecycle / event-handler exception
        // anywhere in the user's tree renders a styled fallback page instead of an HTTP 500. Declared in
        // this generic method so TApp's DynamicallyAccessedMembers annotation flows into the closure —
        // EnsureRuntimeMapped is deliberately non-generic (its double-map guard is per host, not per TApp).
        // A chain carries properties and DI services; the app instance is a runtime constructor argument,
        // and this is the root, so there is no parent render context whose GetOrCreate a chain would route
        // through. RASK014's reason to exist is absent here.
#pragma warning disable RASK014
        Func<IServiceProvider, Component> appFactory =
            sp => new RootErrorBoundary(ActivatorUtilities.CreateInstance<TApp>(sp));
#pragma warning restore RASK014

        // Applications mounted under their own prefix — the operator console at /_rask is the one that
        // exists. Read from the container rather than named here, because Rask.Server cannot see the
        // packages that declare them and must not: it would drag EF and the batteries into a host that
        // wanted neither.
        var selector = new RaskRootSelector(
            appFactory,
            endpoints.ServiceProvider.GetServices<RaskMountedApp>().ToArray());

        EnsureRuntimeMapped(endpoints, pathBaseNormalized, selector);

        // Scope the catch-all SPA route under the prefix when set. The pattern
        // default ("/{**path}") is interpreted relative to the prefix root, so
        // a request to /sub/users/42 matches as Path.Value="/sub/users/42";
        // the handler strips the prefix before resolving against user routes
        // (which are registered as "/users/{id}").
        var scopedPattern = pathBaseNormalized.Length == 0
            ? pattern
            : pathBaseNormalized + (pattern.StartsWith('/') ? pattern : "/" + pattern);

        // Hoisted so the same handler can serve the host's catch-all AND each mounted application's
        // pattern. It already decides which application owns a request from the path, so mapping it more
        // than once adds a way in rather than a second behaviour.
        var pageHandler = (RequestDelegate)(async httpContext =>
        {
            var store = httpContext.RequestServices.GetRequiredService<LiveSessionStore>();
            var limits = httpContext.RequestServices.GetRequiredService<RaskServerLimits>();
            var path = StripPathBase(httpContext.Request.Path.Value ?? "/", pathBaseNormalized);
            var user = httpContext.User ?? new ClaimsPrincipal(new ClaimsIdentity());

            // The route table says whether this path fell through to the not-found page; it does
            // NOT say whether the user will see it. An app whose root renders directly still
            // resolves — the fallback is always registered — but mounts no Router, so the chain is
            // never rendered and the URL is incidental. Confirmed against the render below.
            // Which application owns this path decides BOTH the table it resolves against and the root
            // built for it. Answered together, because a root rendered against another application's
            // routes resolves perfectly and shows the wrong thing.
            var appFactoryForPath = selector.FactoryFor(path);
            var matched = RouteResolver.TryResolve(selector.RoutesFor(path), path, out var chain, out var isNotFound);
            var notFoundPage = isNotFound && matched && chain.Count > 0 ? chain[^1] : null;

            if (matched)
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
            //
            // With static pages on, the slot cannot be reserved up front: whether this page needs a
            // session at all is a property of the render that has not happened yet. The tree is
            // built detached and admitted afterwards, so a page that turns out to need nothing live
            // costs no slot. The cheap AtCapacity probe keeps a saturated host from doing the work
            // anyway; the authoritative check is still the atomic one, at TryRegister below.
            // With server interactivity off, every page is a document whether or not the walk found
            // a reason to keep one — there is nothing for a session to be for.
            var staticPages = limits.StaticPages || !limits.ServerInteractivity;
            var session = staticPages
                ? (store.IsDraining || store.AtCapacity ? null : store.CreateDetached(appFactoryForPath))
                : store.TryCreate(appFactoryForPath);
            if (session is null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                // Both refusals are 503, but they mean different things to whatever is retrying. A
                // draining host is being replaced right now and its stand-in is already up, so a second
                // is the honest hint; a host at capacity needs longer than that to free a slot. No-store
                // on the drain path so a shared cache can never pin this page after the swap.
                if (store.IsDraining)
                {
                    httpContext.Response.Headers.RetryAfter = "1";
                    httpContext.Response.Headers.CacheControl = "no-store";
                    await httpContext.Response.WriteAsync("Server is shutting down; please retry shortly.")
                        .ConfigureAwait(false);
                    return;
                }

                httpContext.Response.Headers.RetryAfter = "5";
                await httpContext.Response.WriteAsync("Server is at session capacity; please retry shortly.")
                    .ConfigureAwait(false);
                return;
            }
            session.Services.GetRequiredService<SessionUserProvider>().Set(user);
            var routeState = session.Services.GetRequiredService<RouteState>();
            routeState.Path = path;
            routeState.Query = AdaptQuery(httpContext.Request.Query);

            // The visitor's language, seeded from the request alongside their identity and route, and
            // BEFORE the first render below — so the page is built in their language rather than
            // rendered in the default and corrected in a second frame they would see flash.
            if (ServerCultureNegotiation.TryNegotiate(
                    httpContext.Request, httpContext.RequestServices, out var culture))
            {
                ServerCultureNegotiation.Apply(session.Services, culture);
                ServerCultureNegotiation.Persist(
                    httpContext.Response,
                    culture,
                    httpContext.RequestServices.GetRequiredService<RaskCultureOptions>());
            }
            // Render the GET shell and seed both baselines: the dedup baseline so a no-op
            // click after hello dedups against the HTML the browser already has (mirroring
            // WASM's InitialRenderAsync / `_lastAppliedHtml`), AND the diff-codec frame
            // baseline so the FIRST interactive WS render ships a diff instead of the whole
            // document. See LiveSession.RenderInitialRoot.
            // The page may shape the response only while this render is running. Closed in the
            // finally so a later event handler — which runs long after these bytes are gone —
            // throws instead of setting a status nobody will ever read.
            var pageResponse = session.Services.GetRequiredService<ServerPageResponse>();
            var navigator = session.Services.GetRequiredService<Navigator>();
            pageResponse.Phase = PageResponsePhase.Initial;
            string html;
            // Navigation is legal for the duration of this render and becomes a real redirect
            // below, so a page can decide on load that the user belongs elsewhere using the same
            // NavigateTo it would call from a handler. Closed straight after, so a background
            // render cannot navigate a request that no longer exists.
            using (navigator.EnterInitialRender())
            {
                try
                {
                    // Awaited, so a page that loads its data in OnMountAsync ships that data rather
                    // than its placeholder. Falls back to the synchronous render when disabled.
                    html = await session
                        .RenderInitialRootAsync(limits.InitialRenderQuiescenceTimeout)
                        .ConfigureAwait(false);
                }
                finally
                {
                    pageResponse.Phase = PageResponsePhase.None;
                }
            }

            // A page that navigated during its own render is telling us the user belongs somewhere
            // else. Answering 302 costs one response instead of a whole page the client immediately
            // navigates away from — and unlike a client-side hop, a crawler and a cache both
            // understand it. The session goes too: nothing will ever connect to this page.
            if (navigator.TryConsumeHistory(out var redirectUrl, out _))
            {
                store.Remove(session.Id);
                httpContext.Response.StatusCode = StatusCodes.Status302Found;
                // Sanitized on the way out even though NavigateTo takes a local path by contract:
                // this value reaches a Location header, and a header is exactly where an unchecked
                // path becomes an open redirect.
                httpContext.Response.Headers.Location =
                    LiveOptions.PathBase + LocalUrl.Sanitize(redirectUrl);
                // Never cacheable. A redirect computed from runtime state — a flag, a tenant, an
                // experiment — that a browser pinned would be unrecoverable without changing the URL.
                httpContext.Response.Headers.CacheControl = "no-store";
                return;
            }
            // The verdict. Everything the walk saw has been accumulated by now; a page that
            // recorded no reason at all needs no connection, so it can be served as a plain
            // document. Development keeps the session either way, so `rask dev` still repaints on
            // an edit and the audit warning below has a socket to have been worth checking.
            // A page may ask to be served static even where the app default is interactive. Honoured
            // only from the routed page itself or the app root: letting an arbitrary helper deep in a
            // tree force a whole page static would be a very quiet way to break it.
            var declaredStatic = notFoundPage is null
                                 && chain.Count > 0
                                 && DeclaredRenderModes.Of(chain[^1]) == RenderMode.Static;

            // Gated as a whole rather than folded in as another reason: every clause below assumes a
            // session is AVAILABLE, and with server interactivity off none of them can be honoured —
            // including the Development one, which would otherwise make every page live while you are
            // developing the very thing you turned off.
            var interactive = limits.ServerInteractivity
                              && (!(staticPages || declaredStatic)
                                  || session.RequiresLiveSession
                                  || session.LastRenderFaulted
                                  // A JS call issued from a continuation AFTER the walk is invisible
                                  // to the render context, but it is still queued waiting for a frame
                                  // that only a socket can carry.
                                  || session.JsInvokes.HasPending
                                  || LiveOptions.IsDevelopment == true);

            // A page that asked to be static and turned out to need a connection keeps the connection —
            // a request, not a command. Reported, because the two facts contradict each other and the
            // author asked for the one that would have broken the page.
            if (declaredStatic && session.RequiresLiveSession)
            {
                RaskDiagnostics.Report(
                    RaskLogLevel.Warning,
                    "Rask.Ssr",
                    $"{chain[^1].Name} declares [RenderMode(RenderMode.Static)] but its render needs a "
                    + $"live connection ({session.InteractivityReasons}), so it kept one. Serving it static "
                    + "would have left that part of the page inert. Remove the attribute, or remove what "
                    + "needs the connection.");
            }

            // data-rask-dev is the client-side gate for every dev-only frame. Resolved per request
            // from the same predicate that decides whether to subscribe at all, so the two can't
            // disagree; in production it is never emitted and those branches stay unreachable.
            var dev = IsDevHotReloadEnabled(httpContext.RequestServices);
            // Where to ask about build status when the socket drops (#603). Read from the environment
            // because the only thing that can answer is `rask dev`, which is the process that launched
            // this one — and it is stamped onto the page rather than pushed over the socket because the
            // question only arises once that socket is gone.
            string content;
            if (interactive)
            {
                // Admit the session now. Under static pages it was built detached, so this is where
                // the cap is actually enforced — and a refusal here means the work is already done,
                // which is the honest cost of not knowing the answer until the render was over.
                if (staticPages && !store.TryRegister(session))
                {
                    await store.DiscardAsync(session).ConfigureAwait(false);
                    httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    httpContext.Response.Headers.RetryAfter = "5";
                    httpContext.Response.Headers.CacheControl = "no-store";
                    await httpContext.Response
                        .WriteAsync("Server is at session capacity; please retry shortly.")
                        .ConfigureAwait(false);
                    return;
                }

                content = LivePayload.InjectWasmBundleAttr(
                    LivePayload.InjectRootAttr(
                        html, session.Id, dev, dev ? Environment.GetEnvironmentVariable("RASK_DEV_STATUS") : null),
                    WasmBootModuleUrl(limits));
            }
            else
            {
                // No session, so no data-rask-root to stamp and no runtime to load. If the tag is
                // not exactly where it should be, TryRemove declines and the page is treated as
                // interactive — serving a document that might still carry a session-bearing script
                // as a cacheable static page is the one outcome worth refusing outright.
                var stripped = RuntimeScriptSplice.TryRemove(html, LiveOptions.PathBase);
                if (stripped is null)
                {
                    interactive = true;
                    if (staticPages && !store.TryRegister(session))
                    {
                        await store.DiscardAsync(session).ConfigureAwait(false);
                        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        httpContext.Response.Headers.RetryAfter = "5";
                        httpContext.Response.Headers.CacheControl = "no-store";
                        await httpContext.Response
                            .WriteAsync("Server is at session capacity; please retry shortly.")
                            .ConfigureAwait(false);
                        return;
                    }

                    content = LivePayload.InjectWasmBundleAttr(
                        LivePayload.InjectRootAttr(
                            html, session.Id, dev,
                            dev ? Environment.GetEnvironmentVariable("RASK_DEV_STATUS") : null),
                        WasmBootModuleUrl(limits));
                }
                else
                {
                    content = stripped;
                }
            }

            httpContext.Response.ContentType = "text/html; charset=utf-8";
            // A page that crashed is not a 200. The root boundary catches the exception and renders the
            // error document, so without this the response looked entirely healthy to every cache,
            // crawler and uptime check (#607). The body is unchanged — the error page is still served,
            // and the live session still attaches, so "Try again" and the reload button both work.
            if (session.LastRenderFaulted)
            {
                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }
            else if (pageResponse.Status is { } pageStatus)
            {
                // Below the faulted check on purpose: a page that threw does not get to claim it
                // succeeded, and the error document is what is actually being served. Above the
                // not-found check so a page can deliberately answer 200 there (a soft 404).
                httpContext.Response.StatusCode = pageStatus;
            }
            else if (notFoundPage is not null && session.LastRenderMounted(notFoundPage))
            {
                // The not-found page renders perfectly ordinary HTML, so without this the response
                // told every cache, crawler and uptime check that a missing page was fine — the
                // same defect #607 fixed for a crashed one. The body is unchanged and the live
                // session still attaches, so navigation off the page still works.
                //
                // Gated on the page actually having been MOUNTED, not merely resolved: an app that
                // renders its root directly resolves the fallback too, and 404-ing every path such
                // an app serves would be a far worse lie than the one being fixed.
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            }

            // The shell embeds the session id (data-rask-root), which is the de-facto bearer
            // for the WS / upload / download endpoints. Forbid any shared-proxy / bfcache /
            // history caching so an authenticated user's session id can't be persisted and
            // replayed by another principal.
            var cache = ShellCachePolicy.For(
                interactive,
                (httpContext.User?.Identity?.IsAuthenticated == true)
                || session.Services.GetRequiredService<SessionUserProvider>()
                    .Current.Identity?.IsAuthenticated == true,
                session.LastRenderFaulted,
                httpContext.Response.StatusCode);
            httpContext.Response.Headers.CacheControl = cache.CacheControl;
            if (cache.Pragma is { } pragma)
            {
                httpContext.Response.Headers.Pragma = pragma;
            }

            if (cache.Vary is { } vary)
            {
                // APPENDED, never assigned. Culture negotiation runs earlier in this same handler and
                // may already have set `Vary: Accept-Language` — overwriting it would let a cache
                // serve one language's page to a visitor who asked for another. Neither change has
                // that bug alone, which is exactly why it is worth stating here.
                var existing = httpContext.Response.Headers.Vary.ToString();
                httpContext.Response.Headers.Vary = existing.Length == 0
                    ? vary
                    : existing.Contains(vary, StringComparison.OrdinalIgnoreCase)
                        ? existing
                        : existing + ", " + vary;
            }

            // Discarded BEFORE the write, not after: WriteAsync to a slow client can take seconds,
            // and everything below reads only the string. Holding a DI scope and a component tree
            // open for the duration of someone's bad connection is pure waste.
            if (!interactive)
            {
                // Marked before the teardown so a later push — the one symptom of the failure mode
                // detection cannot see — is reported rather than swallowed by the ordinary
                // disposed-session early-return.
                session.MarkDiscardedAsStatic();
                if (staticPages)
                {
                    // Built detached, so it was never registered and owns no capacity slot.
                    await store.DiscardAsync(session).ConfigureAwait(false);
                }
                else
                {
                    // A page can declare itself static even where the app has not turned static pages
                    // on, and then the session came from TryCreate — registered, holding a slot. It has
                    // to be removed rather than discarded: discarding leaves it in the store, which
                    // disposes it a second time when its grace elapses.
                    store.Remove(session.Id);
                }
            }

            await httpContext.Response.WriteAsync(content).ConfigureAwait(false);
            if (!interactive)
            {
                return;
            }

            // Schedule cleanup in case no WS ever connects for this session.
            // Browsers / probes can hit the catch-all for resources that don't
            // need a live session (favicon.ico, robots.txt, scanner traffic) —
            // without this guard those sessions stay in the store forever.
            // Uses the SHORT unconnected grace: the runtime connects within ~1s, so a session
            // that never sends `hello` is almost certainly a probe / abandoned load and should
            // not pin a DI scope + tree for the full 30s reconnect window. A real hello cancels
            // this removal (LiveSessionStore.Get) and DetachSocket later re-arms the full grace.
            store.ScheduleRemoval(session.Id, limits.UnconnectedSessionGracePeriod);
        });

        endpoints.MapGet(scopedPattern, pageHandler);

        // A mounted application needs its own endpoint when the host's pattern does not reach it. The
        // default catch-all does, and ASP.NET prefers the more specific route either way, so this is
        // what makes the console work on a host whose own pattern is narrow — a wasm-hosted app, where
        // the SPA fallback would otherwise swallow /_rask.
        foreach (var mount in selector.Mounts)
        {
            var mountPattern = pathBaseNormalized.Length == 0
                ? mount.Pattern
                : pathBaseNormalized + (mount.Pattern.StartsWith('/') ? mount.Pattern : "/" + mount.Pattern);

            endpoints.MapGet(mountPattern, pageHandler);
        }

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

    private static void EnsureRuntimeMapped(
        IEndpointRouteBuilder endpoints, string pathBase, RaskRootSelector selector)
    {
        var marker = endpoints.ServiceProvider.GetRequiredService<RaskLiveMarker>();
        if (marker.RuntimeMapped)
        {
            return;
        }

        marker.RuntimeMapped = true;

        // Registered as a RequestDelegate (services resolved from ctx.RequestServices) rather than a
        // minimal-API Delegate, so it does NOT go through RequestDelegateFactory — which is
        // RequiresDynamicCode and, for a library-registered endpoint (not covered by the app's Request
        // Delegate Generator), crashes at startup under NativeAOT. See the other framework endpoints below.
        endpoints.Map(pathBase + WebSocketPath, (RequestDelegate)(async ctx =>
            {
                var store = ctx.RequestServices.GetRequiredService<LiveSessionStore>();
                // Resolve the per-host safety limits once per connection (not per frame) — the receive
                // loop reads them via instance fields on the hot path.
                var limits = ctx.RequestServices.GetRequiredService<RaskServerLimits>();

                // Server interactivity off means no page may become live, so this endpoint has nothing
                // to serve. Answered as 404 rather than 400 or 426: to anything probing, an app that
                // cannot go live should be indistinguishable from one that never had a socket. Refused
                // here rather than left unmapped so the mapping stays one shape — the marker below and
                // the other framework endpoints are registered together.
                if (!limits.ServerInteractivity)
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

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
                // The socket's lifetime hangs off the drain's HARD deadline, not off ApplicationStopping.
                // This one substitution is what makes a graceful shutdown possible at all: this token
                // becomes LiveSession._socketCt, so while it was ApplicationStopping every send — the
                // shutdown announcement included — threw the instant SIGTERM landed, and every in-flight
                // handler was cancelled before it could finish. Now it trips only when the drain budget
                // is spent, which is also when the ws.Abort() registered below becomes the right answer.
                var drain = ctx.RequestServices.GetRequiredService<RaskDrainCoordinator>();
                using var socketScope = drain.TrackSocket();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    ctx.RequestAborted, drain.HardStopping);
                var resume = ctx.RequestServices.GetRequiredService<SessionResumeSupport>();

                // Negotiated here, from the upgrade request, because that is the last point at which its
                // headers and cookies exist. It is only USED if this socket ends up rebuilding a session
                // (the resume path), which happens in a fresh DI scope much later.
                ServerCultureNegotiation.TryNegotiate(ctx.Request, ctx.RequestServices, out var wsCulture);

                await RunSocketLoop(ws, store, limits, wsUser, resume, selector, linked.Token,
                    drain.HardStopping, wsCulture);
            }));

        var script = LoadEmbeddedScript();
        endpoints.MapGet(pathBase + RuntimePath, (RequestDelegate)(ctx =>
            Results.Text(script, "text/javascript; charset=utf-8").ExecuteAsync(ctx)));

        // PWA endpoints — wired only when AddRaskPwa registered a manifest (off by default). The manifest
        // JSON is rooted at pathBase here (a manifest's members resolve relative to the manifest's own URL,
        // so relative start_url/scope/icons must be made absolute or they'd resolve under /rask/). The SW
        // is served at the app root for full control scope; it handles Web Push and an offline fallback.
        if (endpoints.ServiceProvider.GetService<RaskPwaState>() is { } pwa)
        {
            var manifestJson = pwa.Manifest.ToJson(pathBase);
            endpoints.MapGet(pathBase + ManifestPath, (RequestDelegate)(ctx =>
                Results.Text(manifestJson, "application/manifest+json; charset=utf-8").ExecuteAsync(ctx)));

            var serviceWorker = LoadEmbeddedServiceWorker();
            endpoints.MapGet(pathBase + ServiceWorkerPath, (RequestDelegate)(ctx =>
                Results.Text(serviceWorker, "text/javascript; charset=utf-8").ExecuteAsync(ctx)));
        }

        // Per-component content-addressed asset endpoint. URL is immutable (hash is a
        // SHA-256 prefix of the bytes), so `Cache-Control: immutable` is safe and the
        // browser may reuse the cached entry for the configured `max-age` without
        // revalidating. Range/ETag/HEAD semantics come from Results.Bytes; OPTIONS is
        // handled by routing (405 falls through to ASP.NET's default for non-matching
        // methods). Marked `.AllowAnonymous()` so a host with a fallback authorization
        // policy still serves assets — content-addressed URLs carry no PII, and an unknown
        // hash returns 404 instead of leaking the registered set.
        // Mapped at most once per app. A wasm-hosted app that also mounts the server-rendered dashboard
        // runs both hosts, and Rask.Wasm.Hosting wants this same route; two endpoints with an identical
        // template and precedence are accepted at startup and then throw AmbiguousMatchException on the
        // first request for a scoped stylesheet — an app that boots clean and serves an unstyled 500.
        // Skipping when it is already mapped costs nothing when only one host is present.
        if (!IsEndpointMapped(endpoints, pathBase + "/_rask/a/{hash}.css"))
        {
            endpoints.MapMethods(pathBase + "/_rask/a/{hash}.css", _assetMethods,
                    static ctx => ServeAssetAsync(ctx, AssetKind.Css))
                .AllowAnonymous();
            endpoints.MapMethods(pathBase + "/_rask/a/{hash}.js", _assetMethods,
                    static ctx => ServeAssetAsync(ctx, AssetKind.Js))
                .AllowAnonymous();
        }

        endpoints.MapPost(pathBase + "/_rask/auth/redeem", (RequestDelegate)(ctx =>
                RedeemAuthTicketAsync(ctx, ctx.RequestServices.GetRequiredService<IAuthTicketStore>())))
            .DisableAntiforgery();

        endpoints.MapPost(pathBase + "/_rask/upload/{sessionId}", (RequestDelegate)(ctx =>
                HandleUploadAsync(ctx,
                    (string)ctx.Request.RouteValues["sessionId"]!,
                    ctx.RequestServices.GetRequiredService<LiveSessionStore>(),
                    ctx.RequestServices.GetRequiredService<SessionUploadStore>(),
                    ctx.RequestServices.GetRequiredService<RaskUploadOptions>())))
            .DisableAntiforgery();

        endpoints.MapGet(pathBase + "/_rask/download/{sessionId}/{token}", (RequestDelegate)(ctx =>
            HandleDownloadAsync(ctx,
                (string)ctx.Request.RouteValues["sessionId"]!,
                (string)ctx.Request.RouteValues["token"]!,
                ctx.RequestServices.GetRequiredService<LiveSessionStore>(),
                ctx.RequestServices.GetRequiredService<SessionDownloadStore>())));

        var sessionStore = endpoints.ServiceProvider.GetRequiredService<LiveSessionStore>();
        SubscribeAssetChangedDebounced(sessionStore);
        SubscribeHotReloadApplied(sessionStore, endpoints.ServiceProvider);
    }

    /// <summary>
    ///     Whether the dev-only hot-reload channel is live. Both halves must hold: the process is
    ///     running under <c>dotnet watch</c> (the feature switch is constant-folded to false in a
    ///     normal or published run), and the host is in Development. Production therefore never even
    ///     subscribes, let alone sends.
    /// </summary>
    internal static bool IsDevHotReloadEnabled(IServiceProvider services) =>
        MetadataUpdater.IsSupported &&
        services.GetService<IHostEnvironment>()?.IsDevelopment() == true;

    private static void SubscribeHotReloadApplied(LiveSessionStore sessionStore, IServiceProvider services)
    {
        if (!IsDevHotReloadEnabled(services))
        {
            return;
        }

        RaskHotReload.Applied += () =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await sessionStore.BroadcastAsync(LivePayload.HotReloadAppliedFrame).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    RaskDiagnostics.Report(
                        RaskLogLevel.Warning, "Rask.HotReload",
                        "Rask: hot-reload applied broadcast failed", ex);
                }
            });
        };
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

    private static async Task RunSocketLoop(WebSocket ws, LiveSessionStore store, RaskServerLimits limits,
        ClaimsPrincipal wsUser, SessionResumeSupport resume, RaskRootSelector selector,
        CancellationToken ct, CancellationToken stopping, CultureNegotiation resumeCulture = default)
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

        // Sliding one-second window for the inbound frame-rate cap (limits.MaxInboundFramesPerSecond).
        var rateWindowStartTick = Environment.TickCount64;
        var framesInWindow = 0;

        // One connection-scoped CTS for the idle-socket timeout (null when disabled). Armed across the
        // whole inbound message — first frame and every continuation fragment — and disarmed while the
        // message is dispatched, so a mid-fragment stall is reclaimed too and we don't allocate a CTS
        // per message. CancelAfter just reschedules the one internal timer.
        using var idleCts = limits.IdleSocketTimeout > TimeSpan.Zero
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
                idleCts?.CancelAfter(limits.IdleSocketTimeout);
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
                            if (message.WrittenCount > limits.MaxInboundFrameBytes)
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
                if (limits.MaxInboundFramesPerSecond > 0)
                {
                    var nowTick = Environment.TickCount64;
                    if (nowTick - rateWindowStartTick >= 1000)
                    {
                        rateWindowStartTick = nowTick;
                        framesInWindow = 0;
                    }

                    if (++framesInWindow > limits.MaxInboundFramesPerSecond)
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
                // Match the frame "type" against the UTF-8 literals directly (ValueEquals) instead of
                // materializing a string per frame — this runs on every inbound frame (keystroke,
                // 60 Hz scroll, click), and the string was allocated only to == four constants.
                var hasType = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String;

                if (hasType && t.ValueEquals("hello"u8))
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
                        // This host has never heard of the session. Before the resume protocol that was
                        // the end of it — the client reloaded and the user lost their page. If the client
                        // carries a record we can open, rebuild the page around it instead.
                        var resumeToken =
                            root.TryGetProperty("resume", out var rt) && rt.ValueKind == JsonValueKind.String
                                ? rt.GetString()
                                : null;

                        session = resumeToken is null
                            ? null
                            : TryResumeSession(resumeToken, wsUser, resume, store, metrics, selector);

                        if (session is null)
                        {
                            await SendSessionUnknownAsync(ws, ct).ConfigureAwait(false);
                            return;
                        }

                        // The rebuilt session has a NEW id. The client learns it from the full frame below,
                        // which re-stamps data-rask-root — see LiveSessionBase's full-payload path.
                        session.AttachSocket(ws, ct);
                        session.Services.GetRequiredService<SessionUserProvider>().Set(wsUser);

                        // A resumed session is a NEW DI scope, so its culture starts at the app default.
                        // Without this the visitor's language would silently reset the first time the
                        // host restarted under them — the one moment resume exists to hide.
                        if (resumeCulture.Culture is not null)
                        {
                            ServerCultureNegotiation.Apply(session.Services, resumeCulture);
                        }
                        await session.RenderAndSendAsync(null, false).ConfigureAwait(false);
                        continue;
                    }

                    session.AttachSocket(ws, ct);
                    // Counted here rather than inside AttachSocket: the store owns the number, and the
                    // session has no reason to know a store exists.
                    store.SocketAttached();
                    session.Services.GetRequiredService<SessionUserProvider>().Set(wsUser);

                    // Apply a deferred sign-in/out navigation now that the principal is re-seeded, so the
                    // destination page mounts fresh under the new identity (its OnMountAsync runs against
                    // the redeemed principal). AttachSocket flagged _renderRequestedWhileDetached on this
                    // reconnect, so the FlushPendingRenderAsync below performs a real render against the
                    // updated route. See LiveSession.PendingAuthNavigation.
                    if (session.PendingAuthNavigation is { } authDest)
                    {
                        session.PendingAuthNavigation = null;
                        var routeState = session.Services.GetRequiredService<RouteState>();
                        var (path, query) = SplitUrl(authDest);
                        routeState.Path = path;
                        routeState.Query = query;
                    }

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

                if (hasType && t.ValueEquals("navigate"u8))
                {
                    await HandleNavigateAsync(session, root, ct);
                    continue;
                }

                if (hasType && t.ValueEquals("jsResult"u8))
                {
                    // Round-trip reply for an IJSRuntime.InvokeAsync<T> call. The base
                    // JSRuntime class manages its own pending-task dictionary keyed by the
                    // taskId we passed out in jsInvokes; calling EndInvokeJS with the
                    // serialised [taskId, success, result|error] triple completes the
                    // awaiting ValueTask. No render needed.
                    HandleJsResult(session, root);
                    continue;
                }

                if (hasType && t.ValueEquals("dotNetInvoke"u8))
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
                store.HandlerQueued();
                var pendingBytes = capturedSession.AddPendingHandlerBytes(payloadBytes);
                if ((limits.MaxPendingHandlers > 0 && pending > limits.MaxPendingHandlers)
                    || (limits.MaxPendingHandlerBytes > 0 && pendingBytes > limits.MaxPendingHandlerBytes))
                {
                    capturedSession.DecrementPendingHandlers();
                    store.HandlerDequeued();
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
                    store,
                    capturedSession,
                    capturedHandlerId,
                    capturedRoot,
                    payloadBytes,
                    metrics,
                    limits.HandlerTimeout,
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
                store.SocketDetached();
                store.ScheduleRemoval(session.Id, limits.SessionGracePeriod);
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
        LiveSessionStore store,
        LiveSession session,
        string handlerId,
        JsonElement root,
        long payloadBytes,
        RaskMetrics? metrics,
        TimeSpan handlerTimeout,
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

            await DispatchHandlerAsync(session, handlerId, root, metrics, handlerTimeout, ct).ConfigureAwait(false);

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
            store.HandlerDequeued();
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
        TimeSpan handlerTimeout,
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

        // Action timeout: cancel the dispatch's CancellationToken after handlerTimeout (linked to
        // the socket so a close cancels it too). A handler that threads CancellationToken into its
        // async work unwinds cooperatively; one that ignores it can't be force-aborted (the timeout is
        // still logged + metered). Null when the timeout is disabled, so the default path allocates nothing.
        using var handlerCts = handlerTimeout > TimeSpan.Zero
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;
        handlerCts?.CancelAfter(handlerTimeout);
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

                            // Do NOT navigate routeState here. Setting it to the destination now would
                            // mount the destination page under the PRE-SignIn principal — SessionUserProvider
                            // is only re-seeded on the reconnect handshake — so its OnMountAsync would load
                            // data for the old identity/tenant, and the reconnect re-renders without
                            // remounting (children reconcile by (Type, position), not Key), leaving that
                            // data stale. Park the returnUrl; the hello handler applies it once the reconnect
                            // carries the new cookie, so the page mounts fresh under the new identity. The
                            // client's URL bar still updates immediately via historyUrl below (the separate
                            // history.replace field), behind the "Authenticating…" overlay.
                            session.PendingAuthNavigation = safeReturn;
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
                    $"Rask Live handler '{handlerId}' cancelled after HandlerTimeout ({handlerTimeout})");
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

    /// <summary>
    ///     Opens a resume record and builds a new session around it, or returns <c>null</c> and lets the
    ///     caller fall back to telling the client its session is gone.
    /// </summary>
    /// <remarks>
    ///     The rebuilt session is an ordinary new session in every respect — new id, new DI scope, new
    ///     tree, and a capacity slot taken through the same atomic reservation a GET uses. That last part
    ///     matters more than it looks: a deploy hands every connected client a record at once, so the
    ///     reconnect storm that follows must shed against <c>MaxSessions</c> exactly like fresh traffic
    ///     rather than walking straight past the cap.
    /// </remarks>
    private static LiveSession? TryResumeSession(
        string token,
        ClaimsPrincipal user,
        SessionResumeSupport resume,
        LiveSessionStore store,
        RaskMetrics? metrics,
        RaskRootSelector selector)
    {
        if (resume.Protector is not { } protector)
        {
            return null;
        }

        if (!protector.TryUnprotect(token, user, out var record, out var rejection))
        {
            metrics?.ResumeRejected(rejection.ToString().ToLowerInvariant());
            return null;
        }

        // Split BEFORE the session is created: the path is what says which application this record
        // belongs to, and building the wrong root here is the failure this selector exists to stop.
        var (path, query) = SplitUrl(record!.Url);

        var session = store.TryCreate(selector.FactoryFor(path));
        if (session is null)
        {
            metrics?.ResumeRejected("atcapacity");
            return null;
        }

        // Seed the route and the declared state BEFORE the first render, so the page builds against them
        // rather than rendering a default and then correcting itself in a second frame the user would see.
        var routeState = session.Services.GetRequiredService<RouteState>();
        routeState.Path = path;
        routeState.Query = query;
        session.Services.GetRequiredService<PersistentState>().Restore(record.Entries);

        metrics?.SessionResumed();
        return session;
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
        if (!ScopedAssetBundle.IsContentHash(hash))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        var bytes = ScopedAssetRegistry.GetByHash(hash, kind);
        if (bytes is null)
        {
            // A plain server app has no baked bundle, so this resolves to null and the miss is a 404
            // exactly as before. It is non-null only in a wasm-hosted app that ALSO mounts a
            // server-rendered chain (the operator dashboard): there this endpoint may be the one that
            // won the shared /_rask/a/{hash} route, and the SPA's own assets live in the published
            // bundle rather than in this process's registry. Answering them here is what makes the two
            // hosts' handlers interchangeable, and therefore the order of their UseRask calls irrelevant.
            return ServeBakedBundleFileAsync(ctx, hash, kind);
        }

        // Set headers before invoking Results.Bytes so they are present on the response.
        ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers.Vary = "Accept-Encoding";

        var contentType = kind == AssetKind.Css
            ? "text/css; charset=utf-8"
            : "text/javascript; charset=utf-8";

        // Negotiate br/gzip. The asset is immutable + content-addressed, so each compressed
        // representation is built once and cached (ScopedAssetCompression). The compressed path sets
        // Content-Encoding + an encoding-suffixed ETag; identity keeps Range support.
        var encoding = ScopedAssetCompression.Negotiate(ctx.Request.Headers.AcceptEncoding.ToString());
        if (encoding is not null
            && ScopedAssetCompression.GetEncoded(hash, kind, encoding) is { } enc)
        {
            ctx.Response.Headers.ContentEncoding = encoding;
            return Results.Bytes(enc.Bytes, contentType,
                    entityTag: new EntityTagHeaderValue(enc.Etag))
                .ExecuteAsync(ctx);
        }

        // Results.Bytes wires ETag → If-None-Match (304), HEAD body suppression, and
        // Range request handling (206/416) when enableRangeProcessing is true.
        return Results.Bytes(
                bytes.Value.Utf8.ToArray(),
                contentType,
                enableRangeProcessing: true,
                entityTag: new EntityTagHeaderValue(bytes.Value.Etag))
            .ExecuteAsync(ctx);
    }

    /// <summary>
    ///     Serves the baked <c>/_rask/a/{hash}.{ext}</c> file from a published WASM bundle when this
    ///     process's registry doesn't carry the hash. Mirrors <c>Rask.Wasm.Hosting</c>'s copy — both
    ///     resolve through <see cref="ScopedAssetBundle" />, which is the whole point: the two handlers
    ///     answer identically, so only one of them needs to own the route.
    /// </summary>
    private static async Task ServeBakedBundleFileAsync(HttpContext ctx, string hash, AssetKind kind)
    {
        if (ScopedAssetBundle.FindBakedFile(hash, kind) is not { } path)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers.Vary = "Accept-Encoding";
        ctx.Response.Headers.ETag = "\"" + hash + "\"";
        ctx.Response.ContentType = ScopedAssetBundle.ContentType(kind);

        var encoding = ScopedAssetCompression.Negotiate(ctx.Request.Headers.AcceptEncoding.ToString());
        if (ScopedAssetBundle.FindPrecompressedSibling(path, encoding) is { } sibling)
        {
            ctx.Response.Headers.ContentEncoding = encoding;
            await ctx.Response.SendFileAsync(sibling).ConfigureAwait(false);
            return;
        }

        await ctx.Response.SendFileAsync(path).ConfigureAwait(false);
    }

    /// <summary>
    ///     Whether a route template is already on this <see cref="IEndpointRouteBuilder" />. See the
    ///     twin in <c>Rask.Wasm.Hosting</c> — duplicated rather than shared because the check needs
    ///     <c>RoutePattern</c> and Core takes no ASP.NET routing dependency.
    /// </summary>
    private static bool IsEndpointMapped(IEndpointRouteBuilder endpoints, string rawTemplate)
    {
        foreach (var source in endpoints.DataSources)
        {
            foreach (var endpoint in source.Endpoints)
            {
                if (endpoint is RouteEndpoint route
                    && string.Equals(route.RoutePattern.RawText, rawTemplate, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string LoadEmbeddedScript()
    {
        var asm = typeof(RaskEndpointExtensions).Assembly;
        var name = asm.GetManifestResourceNames()
                       .FirstOrDefault(n => n.EndsWith("rask.js", StringComparison.Ordinal))
                   ?? throw new InvalidOperationException(
                       $"The Rask client script is missing from {asm.GetName().Name} "
                       + $"{asm.GetName().Version}. This is a packaging fault rather than anything in "
                       + "your app: the assembly should embed rask.js. Clear obj/ and bin/ and rebuild; "
                       + "if it persists, the package is damaged — reinstall it, and please report it "
                       + "with the assembly version above.");
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string LoadEmbeddedServiceWorker()
    {
        var asm = typeof(RaskEndpointExtensions).Assembly;
        var name = asm.GetManifestResourceNames()
                       .FirstOrDefault(n => n.EndsWith("rask-sw.js", StringComparison.Ordinal))
                   ?? throw new InvalidOperationException("rask-sw.js embedded resource not found.");
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

    /// <summary>The runtime script tag's exact bytes, for the static-response splice.</summary>
    internal static string RuntimeScriptTag(string pathBase) => ServerRuntimeScript.Tag(pathBase);

    /// <summary>
    ///     The browser bundle's boot module URL for this app, or <c>null</c> when the browser rung is
    ///     off — which is every app that has not asked for it.
    /// </summary>
    /// <remarks>
    ///     Path-based like every other framework asset, so an app hosted under a sub-path fetches its
    ///     own bundle rather than one at the origin root.
    /// </remarks>
    internal static string? WasmBootModuleUrl(RaskServerLimits limits) =>
        limits.WasmBundleUrl is { } bundle ? LiveOptions.PathBase + bundle : null;

    private sealed partial class ServerRuntimeScript : IRaskRuntimeScript
    {
        /// <summary>
        ///     The exact bytes <see cref="Render" /> serializes to, so a static response can remove
        ///     the tag it never needed. The two must agree; a test pins the serializer's output
        ///     against this so they cannot drift.
        /// </summary>
        internal static string Tag(string pathBase) =>
            "<script src=\"" + pathBase + RuntimePath + "\"></script>";

        public Component Render() => Script.Src(LiveOptions.PathBase + RuntimePath);
    }

    internal sealed class RaskLiveMarker
    {
        public bool RuntimeMapped;
    }
}
