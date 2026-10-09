using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.WebSockets;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
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
using Rask.Core.Diagnostics.DevTools;
using Rask.Core.Forms;
using Rask.Core.Globalization;
using Rask.Core.HotReload;
using Rask.Core.Http;
using Rask.Core.Live;
using Rask.Core.Messaging;
using Rask.Core.Routing;
using Rask.Core.ScopedAssets;
using Rask.Hosting.Shared;
using Rask.Server.Authentication;
using Rask.Server.DevTools;
using Rask.Server.Diagnostics;
using Rask.Server.Files;
using Rask.Server.Http;
using Rask.Server.JSInterop;
using Rask.Server.Transport;
using IQueryCollection = Microsoft.AspNetCore.Http.IQueryCollection;
using QueryCollection = Rask.Core.Routing.QueryCollection;
using QueryString = Rask.Core.Routing.QueryString;

namespace Rask.Server;

/// <summary>
///     The endpoints that make an ASP.NET Core app a Rask app: the page routes, the live WebSocket the
///     diff runtime talks over, and the client runtime script. Wired up by <c>MapRask&lt;TApp&gt;()</c>.
/// </summary>
[global::Rask.Core.RaskMarkup]
public static partial class RaskEndpointExtensions
{
    private const string RuntimePath = "/rask/rask.js";

    // The behaviour hooks, which the runtime loads from beside itself when a page first asks for one. The name
    // is the runtime's with one word changed, because that is how the runtime finds it (rask.ts).
    private const string HooksPath = "/rask/rask-hooks.js";
    private const string WebSocketPath = "/rask/ws";

    // The HTTP fallback, for a client whose WebSocket never opened: frames come down the stream, the
    // client's own frames go up as POSTs, and the leave request says a tab is gone without waiting for a timeout.
    private const string StreamPath = "/_rask/stream/{sessionId}";
    private const string SendPath = "/_rask/send/{sessionId}";
    private const string LeavePath = "/_rask/leave/{sessionId}";

    // PWA endpoints, mapped only when AddRaskPwa registered a RaskPwaState. The manifest is under /rask/, and
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
    // serializer call threw at static-init and crashed MapRask before the host could start.
    private static readonly byte[] SessionUnknownPayload =
        Encoding.UTF8.GetBytes("""{"type":"session","status":"unknown"}""");


    // 50ms trailing-edge debounce for ScopedAssetRegistry.AssetChanged. A multi-file edit
    // (or a single hot-reload UpdateApplication burst that re-registers every component
    // back-to-back) generates N events; without coalescing each fires its own
    // RerenderAll. The generation counter snapshot survives only if no newer event
    // arrived during the quiet window — the trailing change wins and we re-render once.
    private static long _assetChangeGen;

    // HTTP methods accepted by the per-component asset endpoint. GET serves the body, and
    // HEAD returns the same headers with no body (handled by Results.Bytes internally).
    // POST/PUT/DELETE/PATCH not listed → ASP.NET returns 405 Method Not Allowed.
    private static readonly string[] _assetMethods = ["GET", "HEAD"];

    /// <summary>
    ///     Registers the Rask server-side live-rendering services (session store, routing,
    ///     authentication, file upload/download, <see cref="IJSRuntime" /> bridge, and the live
    ///     runtime script). Call this in <c>ConfigureServices</c>, then
    ///     <see cref="MapRask{TApp}(WebApplication, string, string)" />
    ///     in the pipeline. The session Rask authenticates is a cookie, and the <c>Rask.Auth</c>
    ///     battery owns that scheme — <c>AddRask</c> itself carries no auth options object.
    /// </summary>
    /// <param name="services">The service collection to add Rask services to.</param>
    /// <param name="configure">
    ///     Optional per-app live-runtime options shared by the Server and WASM runtimes (diff mode,
    ///     path base, session cap, scoped-asset preload). Applied after the <c>Rask:Live</c> configuration
    ///     section, so code wins. When both are absent, framework defaults apply
    ///     (<see cref="Rask.Core.Live.LiveDiffMode.Auto" />, no path base, uncapped).
    /// </param>
    /// <param name="configureServer">
    ///     Optional server-host-only limits (<see cref="RaskServerOptions" />): the WebSocket
    ///     frame-size / frame-rate / pending-handler caps and the session grace periods. Applied after the
    ///     <c>Rask:Server</c> configuration section (<c>Rask__Server__SessionGracePeriod=00:00:20</c> in the
    ///     environment works), so code wins. An out-of-range value fails the host's start, naming the key.
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
        AddOptionsAndKeyRing(services, configure, configureServer);
        AddSessionHosting(services);
        AddSessionServices(services, configureCulture);

        // A test is running this app's Program.cs (Page.Visit): a server that opens no socket, and the started app
        // handed to the test rather than served. Every Rask server app comes through here, however it is wired.
        if (AppCapture.Current is { } capture)
        {
            services.AddSingleton<IServer, CapturedServer>();
            services.AddSingleton<IHostedService>(sp => new CapturedHandOff(
                capture, sp, sp.GetRequiredService<IHostApplicationLifetime>()));
        }

        return services;
    }

    private static void AddOptionsAndKeyRing(
        IServiceCollection services,
        Action<RaskLiveOptions>? configure,
        Action<RaskServerOptions>? configureServer)
    {
        // Per-app live runtime options, bound from Rask:Live and then the callback — code wins over
        // appsettings, appsettings over the defaults (see RaskOptionsRegistration). The framework default for
        // DiffMode is LiveDiffMode.Auto, so a fresh `AddRask()` ships the diff codec out of the box.
        //
        // Nothing is READ here. A host registers IConfiguration through a factory, so the options are built on
        // first use, and every value that used to be copied out at this point is read from the built options
        // where it is needed: the session cap and diff mode by the LiveSessionStore factory below, PathBase and
        // MinifyScopedAssets by MapRask (they back the process-wide content-addressed asset registries, so they
        // stay statics). DiffMode is a per-host value carried on the LiveSessionStore (and handed to each
        // LiveSession), NOT a process-global static — so two hosts in one process, and parallel tests, each
        // render in their own mode instead of racing shared state.
        services.AddRaskOptions<RaskLiveOptions>("Rask:Live", static (section, o) => section.Bind(o), configure,
            validate: null);

        // The server-only WS / grace-period safety limits, bound from Rask:Server and then the callback, and
        // projected into a per-host RaskServerLimits singleton. The singleton — not a process-global static —
        // is the hot-path source of truth: the WS endpoint resolves it once per connection so concurrent hosts
        // each carry their own limits. An out-of-range value fails the host's start, naming the key.
        services.AddRaskOptions<RaskServerOptions>("Rask:Server", static (section, o) => section.Bind(o), configureServer,
            static o => o.Validate());
        services.TryAddSingleton(static sp => RaskServerLimits.From(sp.GetRequiredService<RaskServerOptions>()));

        // The in-page devtools, when this is a Debug build that carries Rask.DevTools. Found by name, so the
        // app writes nothing; inert without the package, folded away entirely in a trimmed Release publish.
        // Whether they switch ON is still the environment's call (Development only), made where they run.
        RaskDevToolsLoader.TryAttach(services);

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
    }

    private static void AddSessionHosting(IServiceCollection services)
    {
        // Stop the hosted services CONCURRENTLY, inside a budget that fits under the deploy's SIGKILL.
        // Sequentially — .NET's default — each pillar's shutdown grace sums to 30s against a window that
        // closes at 20s, so whichever one stops last is killed mid-write, decided by the order of the
        // AddRaskX calls above. See RaskShutdownDefaults; override by configuring HostOptions after AddRask.
        //
        // Rask.Spa.Hosting registers the same pair from the same source-linked types, because its host
        // faces the same two failures. An app serving a SPA with the dashboard on calls both and gets one
        // setup per assembly — they compute identical values, so it is idempotent.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<HostOptions>, RaskShutdownDefaults>());

        // Seals the record a client carries from one session to the next. Live only when the host has Data
        // Protection (WebApplication.CreateBuilder does; a hand-rolled host might not) AND resume is on —
        // absent either, a reconnect to an unknown session falls back to the reload it did before rather
        // than failing the host at startup. Note this is what makes a persisted key ring load-bearing: with
        // the default per-container ring, a record sealed before a redeploy cannot be opened after it —
        // which is what RaskDataProtectionSetup above is for.
        services.TryAddSingleton(static sp =>
        {
            var limits = sp.GetRequiredService<RaskServerLimits>();
            var provider = limits.SessionResume ? sp.GetService<IDataProtectionProvider>() : null;
            return new SessionResumeSupport(
                provider is null ? null : new SessionHandoffProtector(provider, limits.ResumeTokenLifetime));
        });

        // Metrics singleton (Meter "Rask.Server"). TryAdd so a host can pre-register its own.
        services.TryAddSingleton<RaskMetrics>();
        // Per-host shutdown state. Pure state with no dependencies, so the store can read it without a
        // construction cycle; RaskDrainService drives it.
        services.AddSingleton<RaskDrainCoordinator>();
        // Which Server-Sent Events stream each session is on, for the HTTP transport. Per host, never static.
        services.AddSingleton<StreamRegistry>();
        services.AddSingleton(static sp =>
        {
            // Session cap and diff mode are per-store instance values (not statics) so concurrent hosts and
            // tests don't clobber each other through global state.
            var live = sp.GetRequiredService<RaskLiveOptions>();
            return new LiveSessionStore(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetService<IHostApplicationLifetime>(),
                sp.GetService<RaskMetrics>())
            {
                MaxSessions = live.MaxSessions,
                DiffMode = live.DiffMode,
                // Assigned rather than passed: LiveSessionStore's constructor is public and
                // RaskDrainCoordinator is internal, and every directly-constructed test store keeps working
                // (it simply never drains).
                Drain = sp.GetRequiredService<RaskDrainCoordinator>(),
            };
        });
        // Graceful shutdown for the live sessions: announce, settle in-flight handlers, close each socket
        // with a real handshake, dispose awaited. Registered unconditionally — a drain is not an opt-in.
        services.AddHostedService<RaskDrainService>();

        // An app VS Code's F5 launched starts its islands' Vite dev server itself: `rask dev` would have, and
        // under a debugger there is no `rask dev`. Registered only for a dev-session build; whether it acts is
        // decided at startup (Development, and not under dotnet watch).
        Dev.IslandDevServer.Register(services, System.Reflection.Assembly.GetEntryAssembly());
    }

    private static void AddSessionServices(IServiceCollection services, Action<RaskCultureOptions>? configureCulture)
    {
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

        AddVisitorServices(services, configureCulture);
    }

    // `"Cultures": ["en", "hu"]` is the list itself — the same word as `c.Cultures` in code. The named settings sit
    // beside it (`Rask__Cultures__0=en`, `Rask__Cultures__UseCookie=false`), or the whole thing is written as an
    // object: `"Cultures": { "SupportedCultures": [...], "DefaultCulture": "hu" }`.
    private static void BindCultures(IConfigurationSection section, RaskCultureOptions options)
    {
        section.Bind(options);
        foreach (var entry in section.GetChildren())
        {
            if (int.TryParse(entry.Key, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                && !string.IsNullOrWhiteSpace(entry.Value))
            {
                options.SupportedCultures.Add(entry.Value);
            }
        }
    }

    private static void AddVisitorServices(IServiceCollection services, Action<RaskCultureOptions>? configureCulture)
    {
        // Scoped: a DI scope on the server IS a live session, and so a visitor. Registered even when
        // the app configured nothing, because IRaskCulture is a host contract; without a configured
        // culture this is inert. Negotiating one from the request arrives in a later change.
        //
        // The options bind from Rask:Cultures and then configureCulture, registered HERE rather than inside
        // Core's AddRaskCulture: Core is shared with the browser, which has no configuration to bind. Core's
        // own TryAddSingleton of the options is then a no-op, and so is its IsEnabled switch — which is read
        // off the built options by MapRask instead, since nothing is built yet.
        services.AddRaskOptions<RaskCultureOptions>("Rask:Cultures", static (section, o) => BindCultures(section, o),
            configureCulture, validate: null);
        // Before AddRaskCulture, whose remember-nothing fallback is a TryAdd: the cookie is written through
        // Rask.Web, which Core cannot see.
        services.TryAddScoped<IRaskCulturePersistence, Rask.Web.CookieCulturePersistence>();
        services.AddRaskCulture(configure: null, ServiceLifetime.Scoped);
        // Typed browser/device API wrappers — the transport-agnostic Core set, Scoped (one per WebSocket
        // session). Registered via the shared helper (RaskBrowserApis) so the interface → impl list lives in
        // one place instead of being duplicated across the Server and WASM hosts. TryAdd inside the helper
        // lets an app pre-register a better implementation and win. The wake lock and push are MDN's own from
        // Rask.Web (Navigator.WakeLock, PushManager through the service worker AddRaskPwa serves).
        // The remaining browser APIs are intentionally NOT registered on Server: they need transient user
        // activation, a live document/handle, or the installed-PWA instance the WebSocket round-trip loses,
        // so they are provided only by the WASM host (IShare and the rest of the WASM-only set — see
        // RaskWasmBrowserApis). Server can still reach the
        // activation-gated APIs declaratively via Trigger.Gesture — see docs/browser-capabilities.md.
        services.AddCoreBrowserApis(ServiceLifetime.Scoped);
        services.AddScoped<AuthSignIn>();
        services.AddScoped<IAuthSignIn>(sp => sp.GetRequiredService<AuthSignIn>());
        services.AddSingleton<IAuthTicketStore, AuthTicketStore>();
        services.AddSingleton<IRaskRuntimeScript, ServerRuntimeScript>();
        services.AddSingleton<SessionUploadStore>();
        services.AddSingleton<SessionDownloadStore>();
        // Rask:Uploads. A host that registered its own RaskUploadOptions instance before AddRask still wins.
        services.AddRaskOptions<RaskUploadOptions>("Rask:Uploads", static (section, o) => section.Bind(o), configure: null,
            validate: null);
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
    public static WebApplication MapRask<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        this WebApplication app,
        string pattern = "/{**path}",
        string pathBase = "")
        where TApp : Component
    {
        PrepareHost(app);
        ((IEndpointRouteBuilder)app).MapRask<TApp>(pattern, pathBase);
        return app;
    }

    /// <summary>
    ///     Endpoint-routing overload of <see cref="MapRask{TApp}(WebApplication, string, string)" />: maps the
    ///     Rask live endpoints onto an existing <see cref="IEndpointRouteBuilder" /> without touching the
    ///     middleware pipeline (the caller is responsible for <c>UseWebSockets()</c>).
    /// </summary>
    /// <typeparam name="TApp">The root <see cref="Component" /> rendered for every matched route.</typeparam>
    /// <param name="endpoints">The endpoint route builder to map Rask routes on.</param>
    /// <param name="pattern">Catch-all route pattern Rask serves (default <c>/{**path}</c>).</param>
    /// <param name="pathBase">Optional URL prefix; see the <see cref="WebApplication" /> overload.</param>
    /// <returns>The same <paramref name="endpoints" /> instance, for chaining.</returns>
    public static IEndpointRouteBuilder MapRask<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/{**path}",
        string pathBase = "")
        where TApp : Component
    {
        var pathBaseNormalized = ApplyLiveOptions(endpoints, pathBase);

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
            sp => new RootErrorBoundary(ActivatorUtilities.CreateInstance<TApp>(sp))
            {
                // RaskApp's document defaults, on the App's own root only — a mounted app below keeps its own.
                Defaults = sp.GetService<RaskDocumentDefaults>(),
            };
#pragma warning restore RASK014

        MapPages(endpoints, appFactory, pathBaseNormalized, pattern);
        return endpoints;
    }

    private static void PrepareHost(WebApplication app)
    {
        // Route every framework diagnostic (Rask.Core + this host) into the application's logging
        // pipeline. No-ops when no ILoggerFactory is registered, leaving the stderr default in place.
        var loggerFactory = app.Services.GetService<ILoggerFactory>();
        RaskServerDiagnostics.Install(loggerFactory);

        var logger = loggerFactory?.CreateLogger("Rask");
        if (logger is not null)
            Starting(logger, RaskVersion.Current);
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
    }

    /// <summary>
    ///     Maps only the applications mounted under their own prefix — the operator console at <c>/_rask</c>, the
    ///     devtools — for a host whose own UI is not a server-rendered root: <c>RaskApp.Serve()</c>, where a
    ///     WebAssembly client owns every other path through <c>MapRaskSpa</c>'s fallback.
    /// </summary>
    /// <remarks>
    ///     No host catch-all, which is the point: mapping one would take every path from the SPA fallback. With no
    ///     mount registered nothing is mapped at all, not even the live runtime: there is no server-rendered page
    ///     left for it to serve.
    /// </remarks>
    internal static WebApplication MapRaskMounts(this WebApplication app, string pathBase = "")
    {
        // Either way: the path base it settles is the one the storage and push routes mapped after this live under.
        var endpoints = (IEndpointRouteBuilder)app;
        var pathBaseNormalized = ApplyLiveOptions(endpoints, pathBase);
        if (!app.Services.GetServices<RaskMountedApp>().Any())
        {
            return app;
        }

        PrepareHost(app);
        MapPages(endpoints, NoHostApp, pathBaseNormalized, hostPattern: null);
        return app;
    }

    // The root a mounts-only host builds for a path no mount covers. A GET never gets here: only the mounts'
    // own patterns are mapped, and each resolves to its mount. What can is a resume record or an HTTP-transport
    // request naming another path — forged or stale — and for that an empty root is the honest answer: this
    // host has no application of its own to render.
#pragma warning disable RASK014 // The root has no parent render context to construct it through.
    private static Component NoHostApp(IServiceProvider services) => new RootErrorBoundary(new Fragment());
#pragma warning restore RASK014

    // The runtime, the host's catch-all when there is one, and each mounted application's own pattern — shared by
    // MapRask and MapRaskMounts, which differ only in whether the host has a root of its own (hostPattern null).
    private static void MapPages(
        IEndpointRouteBuilder endpoints,
        Func<IServiceProvider, Component> appFactory,
        string pathBaseNormalized,
        string? hostPattern)
    {
        // Applications mounted under their own prefix — the operator console at /_rask is the one that
        // exists. Read from the container rather than named here, because Rask.Server cannot see the
        // packages that declare them and must not: it would drag EF and the batteries into a host that
        // wanted neither.
        var selector = new RaskRootSelector(
            appFactory,
            endpoints.ServiceProvider.GetServices<RaskMountedApp>().ToArray(),
            endpoints.ServiceProvider.GetService<IRaskServerDevTools>());

        EnsureRuntimeMapped(endpoints, pathBaseNormalized, selector);

        // Hoisted so the same handler can serve the host's catch-all AND each mounted application's
        // pattern. It already decides which application owns a request from the path, so mapping it more
        // than once adds a way in rather than a second behaviour.
        var pageHandler = (RequestDelegate)(httpContext => ServePageAsync(httpContext, selector, pathBaseNormalized));

        // Scope the catch-all SPA route under the prefix when set. The pattern
        // default ("/{**path}") is interpreted relative to the prefix root, so
        // a request to /sub/users/42 matches with that whole path, and
        // the handler strips the prefix before resolving against user routes
        // (which are registered as "/users/{id}").
        if (hostPattern is not null)
        {
            endpoints.MapGet(UnderPathBase(pathBaseNormalized, hostPattern), pageHandler);
        }

        // A mounted application needs its own endpoint when the host's pattern does not reach it. The
        // default catch-all does, and ASP.NET prefers the more specific route either way, so this is
        // what makes the console work on a host whose own pattern is narrow — or that has none, like a
        // wasm-hosted app, where the SPA fallback would otherwise swallow /_rask.
        foreach (var mountPattern in selector.Mounts.Select(mount => UnderPathBase(pathBaseNormalized, mount.Pattern)))
        {
            endpoints.MapGet(mountPattern, pageHandler);
        }
    }

    private static string ApplyLiveOptions(IEndpointRouteBuilder endpoints, string pathBase)
    {
        // Normalize once and stash on the static accessor so all downstream URL
        // emission (head asset links, runtime <script> src, download URLs) reads
        // the same prefix. A non-empty value also scopes every Map call below
        // under the prefix so two Rask servers can live side-by-side on one
        // origin behind a reverse proxy.
        //
        // The built options carry the process-wide statics, applied here because this is the first point the
        // options exist — AddRask only registered them. An explicit pathBase argument wins; without one, the
        // configured Rask:Live:PathBase does (an omitted argument used to reset a configured prefix to the root).
        var liveOptions = endpoints.ServiceProvider.GetService<RaskLiveOptions>();
        if (liveOptions?.MinifyScopedAssets is { } minify)
        {
            LiveOptions.MinifyScopedAssets = minify;
        }

        if (endpoints.ServiceProvider.GetService<RaskCultureOptions>() is { SupportedCultures.Count: > 0 })
        {
            RaskCulture.IsEnabled = true;
        }

        var pathBaseNormalized = RaskPath.Normalize(pathBase.Length > 0 ? pathBase : liveOptions?.PathBase ?? "");
        LiveOptions.PathBase = pathBaseNormalized;
        return pathBaseNormalized;
    }

    // The route table says whether this path fell through to the not-found page; it does
    // NOT say whether the user will see it. An app whose root renders directly still
    // resolves — the fallback is always registered — but mounts no Router, so the chain is
    // never rendered and the URL is incidental. Confirmed against the render below.
    // Which application owns this path decides BOTH the table it resolves against and the root
    // built for it. Answered together, because a root rendered against another application's
    // routes resolves perfectly and shows the wrong thing.
    private static async Task ServePageAsync(HttpContext httpContext, RaskRootSelector selector, string pathBaseNormalized)
    {
        var store = httpContext.RequestServices.GetRequiredService<LiveSessionStore>();
        var limits = httpContext.RequestServices.GetRequiredService<RaskServerLimits>();
        var path = StripPathBase(httpContext.Request.Path.Value ?? "/", pathBaseNormalized);
        var user = httpContext.User ?? new ClaimsPrincipal(new ClaimsIdentity());

        var appFactoryForPath = selector.FactoryFor(path);
        var matched = RouteResolver.TryResolve(selector.RoutesFor(path), path, out var chain, out var isNotFound);
        var notFoundPage = isNotFound && matched && chain.Count > 0 ? chain[^1] : null;

        if (matched && !await AuthorizeRouteAsync(httpContext, chain, user).ConfigureAwait(false))
        {
            return;
        }

        if (RefusedPanel(httpContext, path))
        {
            return;
        }

        // Session-cap backstop (RaskLiveOptions.MaxSessions). TryCreate reserves a slot
        // atomically and returns null when over cap, rejecting BEFORE the component tree is
        // built so untrusted GET traffic can't exhaust memory (and a concurrent burst can't
        // race past the cap). Checked after the auth guard above so challenge/forbid
        // redirects (which create no session) still work.
        var session = store.TryCreate(appFactoryForPath);
        if (session is null)
        {
            await RefuseAtCapacityAsync(httpContext, store).ConfigureAwait(false);
            return;
        }

        BindToApplication(session, selector, path);
        var culture = NegotiateCulture(httpContext);

        // Render the GET shell and seed both baselines: the dedup baseline so a no-op click after
        // hello dedups against the HTML the browser already has (mirroring WASM's InitialRenderAsync /
        // `_lastAppliedHtml`), AND the diff-codec frame baseline so the FIRST interactive WS render
        // ships a diff instead of the whole document. See LiveSession.RenderInitialRoot. The render —
        // and every decision it ends in — is PageRender's, so anything else that renders a page
        // reaches the answer this request does.
        var render = await Prerender.PageRender
            .RenderAsync(
                session,
                new Prerender.PageRenderInput(
                    path, AdaptQuery(httpContext.Request.Query), user, culture, chain, notFoundPage),
                limits,
                CancellationToken.None)
            .ConfigureAwait(false);

        await AnswerAsync(httpContext, store, session, render, limits).ConfigureAwait(false);
    }

    // The redirect the page asked for, or the page itself.
    private static async Task AnswerAsync(
        HttpContext httpContext,
        LiveSessionStore store,
        LiveSession session,
        Prerender.PageRenderResult render,
        RaskServerLimits limits)
    {
        if (render.Kind == Prerender.PageRenderKind.Redirect)
        {
            Redirect(httpContext, store, session, render);
            return;
        }

        await WritePageAsync(httpContext, session, render, limits).ConfigureAwait(false);

        // Schedule cleanup in case no WS ever connects for this session.
        // Browsers / probes can hit the catch-all for resources that don't
        // need a live session (favicon.ico, robots.txt, scanner traffic) —
        // without this guard those sessions stay in the store forever.
        // Uses the SHORT unconnected grace: the runtime connects within ~1s, so a session
        // that never sends `hello` is almost certainly a probe / abandoned load and should
        // not pin a DI scope + tree for the full 30s reconnect window. A real hello cancels
        // this removal (LiveSessionStore.Get) and DetachSocket later re-arms the full grace.
        store.ScheduleRemoval(session.Id, limits.UnconnectedSessionGracePeriod);
    }

    // The devtools' own pages decide per request who may open them: Development, this machine, the token the
    // inspected page was rendered with, and that session's owner. Asked after the app's authorization and before
    // a session is reserved, so a refused request costs nothing.
    private static bool RefusedPanel(HttpContext httpContext, string path)
    {
        if (httpContext.RequestServices.GetService<IRaskServerDevTools>()?.RefusePanel(httpContext, path) is not { } refusal)
        {
            return false;
        }

        httpContext.Response.StatusCode = refusal;
        return true;
    }

    // Asks the route's authorization; false when the request was answered with a challenge or a forbid.
    private static async Task<bool> AuthorizeRouteAsync(HttpContext httpContext, IReadOnlyList<Type> chain, ClaimsPrincipal user)
    {
        var authResult = await RouteAuthorizationGuard
            .Evaluate(httpContext.RequestServices, chain, user)
            .ConfigureAwait(false);

        switch (authResult.Outcome)
        {
            case RouteAuthorizationOutcome.Challenge:
                await ChallengeAsync(httpContext, authResult.AuthenticationScheme).ConfigureAwait(false);
                return false;
            case RouteAuthorizationOutcome.Forbid:
                await ForbidAsync(httpContext, authResult.AuthenticationScheme).ConfigureAwait(false);
                return false;
            default:
                return true;
        }
    }

    private static async Task RefuseAtCapacityAsync(HttpContext httpContext, LiveSessionStore store)
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
            await httpContext.Response.WriteAsync("Server is shutting down; please retry shortly.", httpContext.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        httpContext.Response.Headers.RetryAfter = "5";
        await httpContext.Response.WriteAsync("Server is at session capacity; please retry shortly.", httpContext.RequestAborted)
            .ConfigureAwait(false);
    }

    // The visitor's language, negotiated from the request and remembered on the response here,
    // because this handler is what holds a response. It reaches the session inside the render
    // below, alongside the identity and the route and BEFORE the first wave — so the page is built
    // in their language rather than rendered in the default and corrected in a frame they would see.
    private static CultureNegotiation? NegotiateCulture(HttpContext httpContext)
    {
        if (!ServerCultureNegotiation.TryNegotiate(httpContext.Request, httpContext.RequestServices, out var negotiated))
        {
            return null;
        }

        ServerCultureNegotiation.Persist(
            httpContext.Response,
            negotiated,
            httpContext.RequestServices.GetRequiredService<RaskCultureOptions>());
        return negotiated;
    }

    // A page that navigated during its own render is telling us the user belongs somewhere
    // else. Answering 302 costs one response instead of a whole page the client immediately
    // navigates away from — and unlike a client-side hop, a crawler and a cache both
    // understand it. The session goes too: nothing will ever connect to this page.
    private static void Redirect(
        HttpContext httpContext, LiveSessionStore store, LiveSession session, Prerender.PageRenderResult render)
    {
        store.Remove(session.Id);
        httpContext.Response.StatusCode = StatusCodes.Status302Found;
        httpContext.Response.Headers.Location = render.RedirectLocation;
        // Never cacheable. A redirect computed from runtime state — a flag, a tenant, an
        // experiment — that a browser pinned would be unrecoverable without changing the URL.
        httpContext.Response.Headers.CacheControl = "no-store";
    }

    private static async Task WritePageAsync(
        HttpContext httpContext, LiveSession session, Prerender.PageRenderResult render, RaskServerLimits limits)
    {
        // data-rask-dev is the client-side gate for every dev-only frame. Resolved per request
        // from the same predicate that decides whether to subscribe at all, so the two can't
        // disagree; in production it is never emitted and those branches stay unreachable.
        var dev = IsDevHotReloadEnabled(httpContext.RequestServices);
        // The devtools host script and the panel it frames, when AddRask attached the devtools and they switched on
        // (Development).
        var devTools = httpContext.RequestServices.GetService<IRaskServerDevTools>()?.PageTag(httpContext, session.Id);
        httpContext.Response.ContentType = "text/html; charset=utf-8";
        ApplyPageSecurityHeaders(httpContext.Response.Headers);
        // A page that crashed is not a 200, a page may set its own status, and the not-found page
        // answers 404 once it was actually mounted — PageStatus.Of says why each wins where it
        // does (#607). The body is unchanged whatever the status, and a live session still attaches,
        // so "Try again", the reload button and navigation off a missing page all keep working.
        if (render.StatusCode != StatusCodes.Status200OK)
        {
            httpContext.Response.StatusCode = render.StatusCode;
        }

        // The shell embeds the session id (data-rask-root), which is the de-facto bearer
        // for the WS / upload / download endpoints. Forbid any shared-proxy / bfcache /
        // history caching so an authenticated user's session id can't be persisted and
        // replayed by another principal.
        httpContext.Response.Headers.CacheControl = ShellCachePolicy.CacheControl;
        httpContext.Response.Headers.Pragma = ShellCachePolicy.Pragma;

        // A page that arrives asking for a behaviour hook gets the hooks' script in the same response, so they
        // run straight after the runtime rather than a round trip later (HookBundleTag).
        var html = HookBundleTag.AddTo(render.Html, LiveOptions.PathBase, HooksUrl.Value);

        // Outside development the session id is the only thing stamped onto the render, and that goes
        // straight to UTF-8; the development attributes are composed as a string first.
        if (!dev && devTools is null)
        {
            await PageCompression.WriteLiveAsync(httpContext, html, session.Id, limits.CompressPageHtml)
                .ConfigureAwait(false);
            return;
        }

        var content = Prerender.PageDocument.Live(
            html, session.Id, dev,
            dev ? Prerender.PageDocument.IslandsDevUrl(httpContext.RequestServices) : null, devTools);
        await PageCompression.WriteAsync(httpContext, content, limits.CompressPageHtml).ConfigureAwait(false);
    }

    /// <summary>
    ///     <see cref="AddRask(IServiceCollection, Action{RaskLiveOptions}, Action{RaskServerOptions}, Action{RaskCultureOptions})" />
    ///     under a name that says which host it registers — for an app whose own UI is served by another
    ///     host, such as a single-page app with the operator dashboard mounted beside it.
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
    ///     <see cref="MapRask{TApp}(WebApplication, string, string)" /> under a name only this package
    ///     defines — for an app whose own UI is served by another host.
    ///     <para>
    ///         A single-page app that mounts the operator dashboard serves the app with
    ///         <c>MapRaskSpa</c> and the dashboard's server-rendered chain with this, under its own
    ///         prefix; the name says at the call site which of the two a line is.
    ///     </para>
    /// </summary>
    /// <typeparam name="TApp">The root <see cref="Component" /> rendered for every matched route.</typeparam>
    /// <param name="app">The web application to map endpoints on.</param>
    /// <param name="pattern">Catch-all route pattern Rask serves (default <c>/{**path}</c>).</param>
    /// <param name="pathBase">Optional URL prefix; see the <c>MapRask</c> it forwards to.</param>
    public static WebApplication MapRaskServer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        this WebApplication app,
        string pattern = "/{**path}",
        string pathBase = "")
        where TApp : Component =>
        app.MapRask<TApp>(pattern, pathBase);

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

        TightShutdownLadder(logger, drain, host.ShutdownTimeout);
    }

    private static string UnderPathBase(string pathBase, string pattern)
    {
        if (pathBase.Length == 0)
        {
            return pattern;
        }

        return pattern.StartsWith('/') ? pathBase + pattern : pathBase + "/" + pattern;
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
        endpoints.Map(pathBase + WebSocketPath, (RequestDelegate)(ctx => AcceptSocketAsync(ctx, selector)));

        MapHttpTransport(endpoints, pathBase, selector);

        endpoints.MapGet(pathBase + RuntimePath, (RequestDelegate)Runtime.Value.Serve);
        endpoints.MapGet(pathBase + HooksPath, (RequestDelegate)Hooks.Value.Serve);

        // The in-page devtools' own endpoints, when AddRask attached them (a Debug build carrying
        // Rask.DevTools). Here, beside the runtime they extend, so they are mapped once per app too.
        endpoints.ServiceProvider.GetService<IRaskServerDevTools>()?.MapEndpoints(endpoints, pathBase);

        MapPwa(endpoints, pathBase);
        MapScopedAssets(endpoints, pathBase);
        MapSessionEndpoints(endpoints, pathBase);

        var sessionStore = endpoints.ServiceProvider.GetRequiredService<LiveSessionStore>();
        SubscribeAssetChangedDebounced(sessionStore);
        SubscribeHotReloadApplied(sessionStore, endpoints.ServiceProvider);
    }

    private static async Task AcceptSocketAsync(HttpContext ctx, RaskRootSelector selector)
    {
        var store = ctx.RequestServices.GetRequiredService<LiveSessionStore>();
        // Resolve the per-host safety limits once per connection (not per frame) — the receive
        // loop reads them via instance fields on the hot path.
        var limits = ctx.RequestServices.GetRequiredService<RaskServerLimits>();

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
            new WebSocketAcceptContext { DangerousEnableCompression = true }).ConfigureAwait(false);
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
            drain.HardStopping, wsCulture).ConfigureAwait(false);
    }

    private static void MapPwa(IEndpointRouteBuilder endpoints, string pathBase)
    {
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
    }

    private static void MapScopedAssets(IEndpointRouteBuilder endpoints, string pathBase)
    {
        // Per-component content-addressed asset endpoint. URL is immutable (hash is a
        // SHA-256 prefix of the bytes), so `Cache-Control: immutable` is safe and the
        // browser may reuse the cached entry for the configured `max-age` without
        // revalidating. Range/ETag/HEAD semantics come from Results.Bytes; OPTIONS is
        // handled by routing (405 falls through to ASP.NET's default for non-matching
        // methods). Marked `.AllowAnonymous()` so a host with a fallback authorization
        // policy still serves assets — content-addressed URLs carry no PII, and an unknown
        // hash returns 404 instead of leaking the registered set.
        // Mapped at most once per app. Two chains in one app (MapRask under two prefixes) would each map
        // this route, and two endpoints with an identical template and precedence are accepted at startup
        // and then throw AmbiguousMatchException on the first request for a scoped stylesheet — an app
        // that boots clean and serves an unstyled 500. Skipping when it is already mapped costs nothing.
        if (!IsEndpointMapped(endpoints, pathBase + "/_rask/a/{hash}.css"))
        {
            endpoints.MapMethods(pathBase + "/_rask/a/{hash}.css", _assetMethods,
                    static ctx => ServeAssetAsync(ctx, AssetKind.Css))
                .AllowAnonymous();
            endpoints.MapMethods(pathBase + "/_rask/a/{hash}.js", _assetMethods,
                    static ctx => ServeAssetAsync(ctx, AssetKind.Js))
                .AllowAnonymous();
            endpoints.MapMethods(pathBase + "/_rask/a/{hash}.js.map", _assetMethods, ServeSourceMapAsync)
                .AllowAnonymous();
        }
    }

    private static void MapSessionEndpoints(IEndpointRouteBuilder endpoints, string pathBase)
    {
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

        RaskHotReload.Applied += (_, _) =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await sessionStore.Broadcast(LivePayload.HotReloadAppliedFrame).ConfigureAwait(false);
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
                    await sessionStore.RerenderAll().ConfigureAwait(false);
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

    /// <summary>
    ///     The live protocol over plain HTTP: a Server-Sent Events stream down, POSTs up.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Always mapped, never configured. A WebSocket is what a browser gets when one opens; a network that
    ///         blocks the upgrade — a corporate proxy, some captive portals — would otherwise leave every page
    ///         dead, and a page that cannot connect is not a page. The client decides which one it is on and
    ///         remembers the answer for the tab.
    ///     </para>
    ///     <para>
    ///         The guards are the socket's, applied per request rather than once per connection, because an HTTP
    ///         request carries no upgrade to have checked earlier: the host-only Origin check, the browser's own
    ///         <c>Sec-Fetch-Site</c>, and <c>SameSessionUser</c> on every POST.
    ///     </para>
    /// </remarks>
    private static void MapHttpTransport(IEndpointRouteBuilder endpoints, string pathBase, RaskRootSelector selector)
    {
        // The stream. One per tab, held open for the life of the page.
        endpoints.MapGet(pathBase + StreamPath, (RequestDelegate)(ctx =>
            StreamAsync(ctx, selector))).DisableAntiforgery();

        // The client's frames. One request per interaction, which is the cost of not having a socket.
        endpoints.MapPost(pathBase + SendPath, (RequestDelegate)SendAsync).DisableAntiforgery();

        // "This tab is gone." Sent by pagehide, so a closed tab frees its session now rather than after the
        // grace period — the socket gets this for free from its own close.
        endpoints.MapPost(pathBase + LeavePath, (RequestDelegate)LeaveAsync).DisableAntiforgery();
    }

    // A browser stamps every request with where it came from, and "cross-site" is a page on another site driving
    // this one. Refused beside the Origin check. A missing header — a non-browser client, an older browser — is
    // allowed, the same posture IsSameOrigin takes toward a missing Origin.
    private static bool IsCrossSiteFetch(HttpRequest request) =>
        string.Equals(request.Headers["Sec-Fetch-Site"].ToString(), "cross-site", StringComparison.OrdinalIgnoreCase);

    private static ClaimsPrincipal UserOf(HttpContext ctx) => ctx.User ?? new ClaimsPrincipal(new ClaimsIdentity());

    private static string? StringProperty(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Holds the response open and writes the session's frames to it as events.</summary>
    private static async Task StreamAsync(HttpContext ctx, RaskRootSelector selector)
    {
        if (StreamSessionId(ctx) is not { } sessionId)
        {
            return;
        }

        var store = ctx.RequestServices.GetRequiredService<LiveSessionStore>();
        var limits = ctx.RequestServices.GetRequiredService<RaskServerLimits>();
        var registry = ctx.RequestServices.GetRequiredService<StreamRegistry>();
        var drain = ctx.RequestServices.GetRequiredService<RaskDrainCoordinator>();
        var resume = ctx.RequestServices.GetRequiredService<SessionResumeSupport>();

        // Counted in the drain exactly like a socket: a shutdown waits for these to finish too.
        using var scope = drain.TrackSocket();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, drain.HardStopping);
        var ct = linked.Token;

        ServerCultureNegotiation.TryNegotiate(ctx.Request, ctx.RequestServices, out var culture);

        await OpenEventStreamAsync(ctx.Response, ct).ConfigureAwait(false);

        var generation = registry.NextGeneration();
        var transport = new SseTransport(ctx.Response.BodyWriter, generation, ct);

        // Handed over as values, never spliced into a JSON document: both come straight from the request, and a
        // session id or a resume token carrying a quote would otherwise have been read as the hello's own
        // structure. The resume record travels in a header rather than the query string, because a token in a URL
        // ends up in proxy logs and browser history — and this one rebuilds a page.
        var resumeToken = ctx.Request.Headers["Rask-Resume"].ToString();
        var attach = await AttachAsync(
            sessionId, string.IsNullOrEmpty(resumeToken) ? null : resumeToken, transport, store, limits, UserOf(ctx),
            resume, selector, store.Metrics, culture, ct).ConfigureAwait(false);

        if (attach.Status != AttachStatus.Attached || attach.Session is null)
        {
            await RefuseUnknownSessionAsync(transport, generation, limits, ct).ConfigureAwait(false);
            return;
        }

        var session = attach.Session;
        registry.Set(session.Id, transport);

        try
        {
            // Named AFTER the attach, and naming the session it attached. A resume record rebuilds a lost session
            // under a new id, and the client addresses every POST by id: told the generation first, it would open
            // and flush its queue to the id no server knows any more. Any frame the attach itself sent — a
            // rebuild's render — is already on the wire, and the client holds those until this one arrives.
            await transport.SendAsync(StreamOpenedFrame(generation, session.Id, limits.MaxInboundFrameBytes), ct).ConfigureAwait(false);
            await KeepAliveAsync(transport, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The client went away, or the drain's deadline passed. Both end the stream.
        }
        finally
        {
            await EndStreamAsync(registry, store, session, transport, limits.SessionGracePeriod).ConfigureAwait(false);
        }
    }

    // The session id the stream names, or null — having answered the request — when it may not open one.
    private static string? StreamSessionId(HttpContext ctx)
    {
        if (!IsSameOrigin(ctx.Request) || IsCrossSiteFetch(ctx.Request))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return null;
        }

        var sessionId = (string?)ctx.Request.RouteValues["sessionId"];
        if (string.IsNullOrEmpty(sessionId))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return null;
        }

        return sessionId;
    }

    private static async Task EndStreamAsync(
        StreamRegistry registry, LiveSessionStore store, LiveSession session, SseTransport transport, TimeSpan grace)
    {
        registry.Remove(session.Id, transport);
        transport.Abort();
        await transport.WaitForWritesAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        // Only the connection that still owns the session may arm its grace period: a tab that reconnected has
        // already attached a newer one (#1076). A tab that said it was leaving is not coming back, so its
        // session goes now.
        if (session.DetachTransport(transport))
        {
            session.LastStreamGeneration = transport.Generation;
            store.SocketDetached();
            store.ScheduleRemoval(session.Id, session.Leaving ? TimeSpan.Zero : grace);
        }
    }

    private static async Task OpenEventStreamAsync(HttpResponse response, CancellationToken ct)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        // no-transform is the load-bearing one: a proxy that compresses or buffers this response would hold frames
        // until it had "enough" of them, which for a live page is for ever.
        response.Headers.CacheControl = "no-cache, no-transform";
        response.Headers["X-Accel-Buffering"] = "no";
        await response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    // Nothing to attach to, and the same answer whether the id never existed or belongs to someone else
    // (#1075). The client reloads, exactly as it does on the socket — which it can only do from an open
    // connection, so the stream still opens first, naming no session.
    private static async Task RefuseUnknownSessionAsync(
        SseTransport transport, int generation, RaskServerLimits limits, CancellationToken ct)
    {
        await transport.SendAsync(StreamOpenedFrame(generation, sessionId: null, limits.MaxInboundFrameBytes), ct).ConfigureAwait(false);
        await transport.SendAsync(SessionUnknownPayload, ct).ConfigureAwait(false);
        await transport.CloseAsync(LiveTransportClose.Normal, "session-unknown", ct).ConfigureAwait(false);
    }

    // Alive between renders: a quiet page still has to look connected to every proxy in the path.
    private static async Task KeepAliveAsync(SseTransport transport, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && transport.IsOpen)
        {
            var finished = await Task.WhenAny(
                transport.Completed, Task.Delay(TimeSpan.FromSeconds(15), ct)).ConfigureAwait(false);

            if (finished == transport.Completed)
            {
                break;
            }

            await transport.HeartbeatAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     <c>{"type":"stream","session":…,"limit":B,"generation":N}</c> — the frame that opens an HTTP connection.
    ///     <c>limit</c> is <c>MaxInboundFrameBytes</c>: a POST carries a batch, and the client packs each within it so
    ///     the cap stays a per-frame one, as on the socket, rather than refusing a batch of frames that each fit. Written
    ///     through a JSON writer rather than interpolated: the session id comes from the request's route when the
    ///     attach found nothing, and must not be able to add structure of its own.
    /// </summary>
    private static byte[] StreamOpenedFrame(int generation, string? sessionId, int maxFrameBytes)
    {
        var buffer = new ArrayBufferWriter<byte>(96);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type"u8, "stream"u8);
            if (sessionId is not null)
            {
                writer.WriteString("session"u8, sessionId);
            }

            writer.WriteNumber("limit"u8, maxFrameBytes);
            writer.WriteNumber("generation"u8, generation);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Takes the client's frames for a session whose stream is open.</summary>
    private static async Task SendAsync(HttpContext ctx)
    {
        if (!IsSameOrigin(ctx.Request) || IsCrossSiteFetch(ctx.Request))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var store = ctx.RequestServices.GetRequiredService<LiveSessionStore>();
        var limits = ctx.RequestServices.GetRequiredService<RaskServerLimits>();
        var registry = ctx.RequestServices.GetRequiredService<StreamRegistry>();
        var metrics = store.Metrics;

        var sessionId = (string?)ctx.Request.RouteValues["sessionId"];
        // Peek: a POST must not be what keeps a detached session alive (see LiveSessionStore.Peek).
        var session = sessionId is null ? null : store.Peek(sessionId);
        if (session is null
            || !SameSessionUser(UserOf(ctx), session.Services.GetRequiredService<SessionUserProvider>().Current))
        {
            // The same answer for "no such session" and "not yours", so neither can be probed for.
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // The stream this POST believes it is talking to. A tab that reconnected has a newer one, and an older tab's
        // frames must not reach the session it no longer drives.
        var stream = registry.Current(session.Id, ctx.Request.Headers["Rask-Stream"].ToString());
        if (stream is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        await stream.Inbound.WaitAsync(ctx.RequestAborted).ConfigureAwait(false);
        try
        {
            // Replaced or ended while this request waited its turn — or the tab has since attached another
            // connection, which is the one that speaks for the session now.
            if (!stream.Transport.IsOpen || !session.IsAttached(stream.Transport))
            {
                ctx.Response.StatusCode = StatusCodes.Status409Conflict;
                return;
            }

            await ReceiveFramesAsync(ctx, session, stream.Transport, store, limits, registry, metrics)
                .ConfigureAwait(false);
        }
        finally
        {
            stream.Inbound.Release();
        }
    }

    private static async Task ReceiveFramesAsync(
        HttpContext ctx,
        LiveSession session,
        SseTransport stream,
        LiveSessionStore store,
        RaskServerLimits limits,
        StreamRegistry registry,
        RaskMetrics? metrics)
    {
        if (RefusesBody(ctx, limits, metrics))
        {
            return;
        }

        // Read through the cap rather than trusting Content-Length alone, which a chunked request does not carry: the
        // same bound the socket enforces while it reassembles a message.
        using var body = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            int read;
            while ((read = await ctx.Request.Body.ReadAsync(chunk, ctx.RequestAborted).ConfigureAwait(false)) > 0)
            {
                if (body.Length + read > limits.MaxInboundFrameBytes)
                {
                    RejectTooLarge(ctx, metrics);
                    return;
                }

                await body.WriteAsync(chunk.AsMemory(0, read), ctx.RequestAborted).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        using var doc = ParseBatch(ctx, body);
        if (doc is null || !AdmitBatch(ctx, doc.RootElement, body.Length, session, limits, registry, metrics, out var perFrame))
        {
            return;
        }

        foreach (var frame in doc.RootElement.EnumerateArray())
        {
            if (!IsFrame(frame, out var hasType, out var type))
            {
                continue;
            }

            // The stream's lifetime, not this request's: a handler queued here runs after the 204 has gone.
            var outcome = await ProcessFrameAsync(
                session, frame, hasType, type, perFrame, store, limits, metrics, stream.Lifetime)
                .ConfigureAwait(false);

            if (outcome.Status == FrameStatus.Refused)
            {
                // A breaker tripped. The stream is what ends; this request says so.
                EndForPolicy(ctx, registry, session, outcome.Reason!);
                return;
            }
        }

        ctx.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static bool IsFrame(JsonElement frame, out bool hasType, out JsonElement type)
    {
        type = default;
        hasType = frame.ValueKind == JsonValueKind.Object
                  && frame.TryGetProperty("type", out type) && type.ValueKind == JsonValueKind.String;
        return frame.ValueKind == JsonValueKind.Object;
    }

    private static bool RefusesBody(HttpContext ctx, RaskServerLimits limits, RaskMetrics? metrics)
    {
        if (!ctx.Request.HasJsonContentType())
        {
            ctx.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return true;
        }

        if (ctx.Request.ContentLength > limits.MaxInboundFrameBytes)
        {
            RejectTooLarge(ctx, metrics);
            return true;
        }

        return false;
    }

    private static void RejectTooLarge(HttpContext ctx, RaskMetrics? metrics)
    {
        metrics?.FrameRejected("size");
        ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
    }

    private static JsonDocument? ParseBatch(HttpContext ctx, MemoryStream body)
    {
        try
        {
            return JsonDocument.Parse(body.GetBuffer().AsMemory(0, (int)body.Length));
        }
        catch (JsonException)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return null;
        }
    }

    // Whether the batch may be dispatched; when not, the request has been answered.
    private static bool AdmitBatch(
        HttpContext ctx,
        JsonElement batch,
        long bodyLength,
        LiveSession session,
        RaskServerLimits limits,
        StreamRegistry registry,
        RaskMetrics? metrics,
        out int perFrame)
    {
        perFrame = 0;

        // An array, because a client batches whatever piled up while a POST was in flight — which is how
        // arrival order survives a transport that has no ordering of its own.
        if (batch.ValueKind != JsonValueKind.Array)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return false;
        }

        var count = batch.GetArrayLength();
        if (count == 0)
        {
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
            return false;
        }

        // The socket's frame-rate cap, counted per batch. A flood ends the stream the way it ends a socket, so
        // the client reconnects against the intact session and resumes from current state.
        if (limits.MaxInboundFramesPerSecond > 0
            && !registry.TryAdmit(session.Id, count, limits.MaxInboundFramesPerSecond))
        {
            metrics?.FrameRejected("rate");
            EndForPolicy(ctx, registry, session, "frame rate");
            return false;
        }

        // The backlog cap needs a byte count per frame, not an exact one, and dividing the body keeps this
        // allocation-free — measuring each frame would re-serialise it.
        perFrame = (int)Math.Max(1, bodyLength / count);
        return true;
    }

    private static void EndForPolicy(HttpContext ctx, StreamRegistry registry, LiveSession session, string reason)
    {
        registry.Close(session.Id, LiveTransportClose.PolicyViolation, reason);
        ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
    }

    /// <summary>Frees a session whose tab has gone, without waiting out its grace period.</summary>
    private static Task LeaveAsync(HttpContext ctx)
    {
        if (!IsSameOrigin(ctx.Request) || IsCrossSiteFetch(ctx.Request))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        var store = ctx.RequestServices.GetRequiredService<LiveSessionStore>();
        var registry = ctx.RequestServices.GetRequiredService<StreamRegistry>();

        var sessionId = (string?)ctx.Request.RouteValues["sessionId"];
        var session = sessionId is null ? null : store.Peek(sessionId);
        if (session is null
            || !SameSessionUser(UserOf(ctx), session.Services.GetRequiredService<SessionUserProvider>().Current))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        // Only the tab that still owns the stream may end it: a request from a tab that has already been replaced
        // would otherwise close the page its successor is driving. An ordinary ending, not a violation — and the
        // stream's cleanup frees the session at once rather than after the grace period.
        var generation = ctx.Request.Headers["Rask-Stream"].ToString();
        if (registry.IsCurrent(session.Id, generation))
        {
            session.Leaving = true;
            registry.Close(session.Id, LiveTransportClose.Normal, "leave");
        }
        else if (!session.HasOpenTransport
                 && int.TryParse(generation, NumberStyles.Integer, CultureInfo.InvariantCulture, out var named)
                 && named == session.LastStreamGeneration)
        {
            // A closing tab tears its stream down while this request is on its way, so the stream's cleanup
            // often runs first and has already armed the grace period. The generation still names the last
            // stream this session had, and nothing has attached since: it is the same tab, and it has gone.
            store.ScheduleRemoval(session.Id, TimeSpan.Zero);
        }

        ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        return Task.CompletedTask;
    }

    private static async Task RunSocketLoop(WebSocket ws, LiveSessionStore store, RaskServerLimits limits,
        ClaimsPrincipal wsUser, SessionResumeSupport resume, RaskRootSelector selector,
        CancellationToken ct, CancellationToken stopping, CultureNegotiation resumeCulture = default)
    {
        using var abortReg = AbortWhen(ws, stopping);
        var metrics = store.Metrics;

        // One transport for this connection: what the session sends through, and the identity the detach
        // below compares against.
        var transport = new WebSocketTransport(ws);
        LiveSession? session = null;
        using var reader = new SocketReader(ws, limits, metrics, ct);

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                if (await reader.ReceiveAsync().ConfigureAwait(false) is not { } payload)
                {
                    break;
                }

                using var doc = OpenFrame(payload, session, out var hasType, out var t);
                if (doc is null)
                {
                    continue;
                }

                var root = doc.RootElement;
                if (hasType && t.ValueEquals("hello"u8))
                {
                    (var stop, session) = await HelloAsync(
                        ws, root, session, transport, store, limits, wsUser, resume, selector, resumeCulture, ct)
                        .ConfigureAwait(false);
                    if (stop)
                    {
                        break;
                    }

                    continue;
                }

                // A breaker tripped. Close politely: the client reconnects (hello) against the intact
                // session and resumes from current state.
                if (Speaks(session, transport)
                    && await ProcessFrameAsync(session, root, hasType, t, payload.Length, store, limits, metrics, ct)
                        .ConfigureAwait(false) is { Status: FrameStatus.Refused } refused)
                {
                    await ClosePolicyViolationAsync(ws, refused.Reason!).ConfigureAwait(false);
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            // The host is stopping or the idle timeout fired, or the client dropped the connection mid-frame: the
            // loop ends, finally detaches, and the session waits out its grace period.
        }
        finally
        {
            await EndSocketAsync(ws, session, transport, store, limits).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     One socket's inbound side: its receive buffers, its idle timer and its frame-rate window, allocated
    ///     once per connection so that reading a message allocates nothing.
    /// </summary>
    private sealed class SocketReader(
        WebSocket ws, RaskServerLimits limits, RaskMetrics? metrics, CancellationToken ct) : IDisposable
    {
        // Rented, and handed back when the socket closes: a reconnect storm reuses the arrays instead of
        // allocating 16 KB per connection.
        private readonly byte[] _buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);

        // Only a message split over several frames needs this, and a browser sends an event as one frame, so
        // it is made on the first split message rather than paid by every socket. One that grew past
        // KeptMessageBytes is let go after its message instead of pinning up to MaxInboundFrameBytes.
        private const int KeptMessageBytes = 64 * 1024;
        private ArrayBufferWriter<byte>? _message;

        // One connection-scoped CTS for the idle-socket timeout (null when disabled). Armed across the
        // whole inbound message — first frame and every continuation fragment — and disarmed while the
        // message is dispatched, so a mid-fragment stall is reclaimed too and we don't allocate a CTS
        // per message. CancelAfter just reschedules the one internal timer.
        private readonly CancellationTokenSource? _idleCts = limits.IdleSocketTimeout > TimeSpan.Zero
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;

        // Sliding one-second window for the inbound frame-rate cap (limits.MaxInboundFramesPerSecond).
        private long _rateWindowStartTick = Environment.TickCount64;
        private int _framesInWindow;

        // Disposed after the receive loop exits. No payload outlives its loop iteration — the JsonDocument over
        // it is disposed there, so anything kept past it was already copied — so the buffer can go back.
        public void Dispose()
        {
            _idleCts?.Dispose();
            ArrayPool<byte>.Shared.Return(_buffer);
        }

        /// <summary>
        ///     Receives one whole message: the payload, or <c>null</c> when the loop should stop — the peer
        ///     closed, went idle, streamed a message past the size cap, or sent too many.
        /// </summary>
        /// <remarks>
        ///     Called once per inbound message, so its state machine comes from a pool rather than the heap:
        ///     the loop stays allocation-free per frame, as it was when this body lived inside it.
        /// </remarks>
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
        public async ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync()
        {
            if (await ReceiveMessageAsync().ConfigureAwait(false) is not { } payload)
            {
                return null;
            }

            if (OverFrameRate())
            {
                metrics?.FrameRejected("rate");
                await ClosePolicyViolationAsync(ws, "frame rate").ConfigureAwait(false);
                return null;
            }

            return payload;
        }

        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
        private async ValueTask<ReadOnlyMemory<byte>?> ReceiveMessageAsync()
        {
            // Arm the idle timer for this whole message-receive cycle; the finally disarms it
            // before dispatch so a slow handler doesn't trip it. A fired timer (not a shutdown)
            // means the client went silent mid-stream — close the socket (the session survives for
            // reconnect under the grace period).
            // The previous message has been dispatched; a split message that grew the accumulator large is let go.
            if (_message is { Capacity: > KeptMessageBytes })
            {
                _message = null;
            }

            _idleCts?.CancelAfter(limits.IdleSocketTimeout);
            var receiveToken = _idleCts?.Token ?? ct;
            try
            {
                var result = await ws.ReceiveAsync(_buffer, receiveToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                // Hot path: single-fragment message. Parse JSON directly from the
                // receive buffer slice — no UTF-8 string decode, no accumulator copy.
                return result.EndOfMessage
                    ? _buffer.AsMemory(0, result.Count)
                    : await ReceiveFragmentsAsync(result.Count, receiveToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (_idleCts is not null && _idleCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                metrics?.FrameRejected("idle");
                await ClosePolicyViolationAsync(ws, "idle timeout").ConfigureAwait(false);
                return null;
            }
            finally
            {
                _idleCts?.CancelAfter(Timeout.InfiniteTimeSpan);
            }
        }

        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
        private async ValueTask<ReadOnlyMemory<byte>?> ReceiveFragmentsAsync(int firstCount, CancellationToken receiveToken)
        {
            var message = _message ??= new ArrayBufferWriter<byte>(_buffer.Length);
            message.ResetWrittenCount();
            if (firstCount > 0)
            {
                message.Write(_buffer.AsSpan(0, firstCount));
            }

            WebSocketReceiveResult result;
            do
            {
                // Same idle token covers continuation fragments, so a client that stalls
                // mid-message is reclaimed too.
                result = await ws.ReceiveAsync(_buffer, receiveToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (result.Count > 0)
                {
                    message.Write(_buffer.AsSpan(0, result.Count));
                }

                // Abort a socket that streams a frame past the cap rather than buffering it
                // whole — bounds per-socket memory against a fragmented-frame DoS.
                if (message.WrittenCount > limits.MaxInboundFrameBytes)
                {
                    metrics?.FrameRejected("size");
                    try { ws.Abort(); }
                    catch { /* already gone: the frame is refused either way */ }

                    return null;
                }
            } while (!result.EndOfMessage);

            return message.WrittenMemory;
        }

        // Inbound frame-rate cap: count every completed receive over a sliding one-second
        // window and close the socket on a flood, before the per-frame parse. Bounds
        // a small-frame CPU DoS that the size cap and handler backpressure don't cover. The
        // client reconnects (hello) against the intact session and resumes from current
        // state, same as the handler-backlog breaker.
        private bool OverFrameRate()
        {
            if (limits.MaxInboundFramesPerSecond <= 0)
            {
                return false;
            }

            var nowTick = Environment.TickCount64;
            if (nowTick - _rateWindowStartTick >= 1000)
            {
                _rateWindowStartTick = nowTick;
                _framesInWindow = 0;
            }

            return ++_framesInWindow > limits.MaxInboundFramesPerSecond;
        }
    }

    // A malformed frame must not tear down the session. The receive loop's only
    // catches are OperationCanceledException / WebSocketException, so an unguarded
    // JsonException here would propagate to the finally and detach the socket —
    // letting one bad (buggy or adversarial) frame drop the whole live session.
    // Skip it and keep serving; the size cap above still bounds memory.
    //
    // A valid-JSON but non-object root (a bare array / number / string) would make the
    // TryGetProperty calls below throw InvalidOperationException — another way one bad
    // frame could tear the session down. Skip it like a malformed frame.
    private static JsonDocument? OpenFrame(
        ReadOnlyMemory<byte> payload, LiveSession? session, out bool hasType, out JsonElement type)
    {
        hasType = false;
        type = default;
        if (payload.Length == 0 || SafeParse(payload) is not { } doc)
        {
            return null;
        }

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            doc.Dispose();
            return null;
        }

        // Only once there is a session to attribute it to: the hello that creates one arrives first.
        if (session is not null && RaskDevToolsHook.Active is { } devTools)
        {
            devTools.FrameReceived(session, payload.Length, root);
        }

        // Match the frame "type" against the UTF-8 literals directly (ValueEquals) instead of
        // materializing a string per frame — this runs on every inbound frame (keystroke,
        // 60 Hz scroll, click), and the string was allocated only to == four constants.
        hasType = root.TryGetProperty("type", out type) && type.ValueKind == JsonValueKind.String;
        return doc;
    }

    private static CancellationTokenRegistration AbortWhen(WebSocket ws, CancellationToken stopping) =>
        stopping.Register(() =>
        {
            try { ws.Abort(); }
            catch { /* the socket is already gone, which is what aborting it was for */ }
        });

    // A socket another hello has replaced speaks for the session no longer. Its loop runs on
    // until the socket itself dies, and a frame from it must not dispatch: that socket was
    // admitted for the principal the session had THEN, which a sign-in on the new socket may
    // have changed since. Dropped rather than closed, so a duplicated tab holding the same id
    // does not fall into a reconnect tug-of-war with the original.
    private static bool Speaks([NotNullWhen(true)] LiveSession? session, WebSocketTransport transport) =>
        session is not null && session.IsAttached(transport);

    /// <summary>
    ///     The session a <c>hello</c> attaches the socket to, or <c>Stop</c> when the socket must end.
    /// </summary>
    private static async Task<(bool Stop, LiveSession? Session)> HelloAsync(
        WebSocket ws,
        JsonElement root,
        LiveSession? session,
        WebSocketTransport transport,
        LiveSessionStore store,
        RaskServerLimits limits,
        ClaimsPrincipal wsUser,
        SessionResumeSupport resume,
        RaskRootSelector selector,
        CultureNegotiation resumeCulture,
        CancellationToken ct)
    {
        // One session per socket. The client sends exactly one hello per connection, so a
        // second is a protocol violation — and honouring it leaked: it re-pointed this loop
        // at another session without detaching the first, and with a resume token it built
        // one more session per frame (#1059).
        if (session is not null)
        {
            store.Metrics?.FrameRejected("hello");
            await ClosePolicyViolationAsync(ws, "hello").ConfigureAwait(false);
            return (true, session);
        }

        var attach = await AttachAsync(
            StringProperty(root, "session"), StringProperty(root, "resume"), transport, store, limits,
            wsUser, resume, selector, store.Metrics, resumeCulture, ct).ConfigureAwait(false);

        if (attach.Status == AttachStatus.Unknown)
        {
            // Nothing to attach to, and nothing that says whether the id ever existed.
            await SendSessionUnknownAsync(ws, ct).ConfigureAwait(false);
            return (true, null);
        }

        // Ignored leaves the socket waiting for a hello it can attach.
        return (false, attach.Status == AttachStatus.Ignored ? null : attach.Session);
    }

    private static async Task EndSocketAsync(
        WebSocket ws, LiveSession? session, WebSocketTransport transport, LiveSessionStore store, RaskServerLimits limits)
    {
        // Only when this loop's socket is still the attached one. A tab that reconnected before the
        // server noticed this socket die has attached a new one already: detaching, counting the
        // disconnect or arming removal here would do all three to the live connection (#1076).
        if (session is not null && session.DetachTransport(transport))
        {
            store.SocketDetached();
            store.ScheduleRemoval(session.Id, limits.SessionGracePeriod);
        }

        if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
        {
            using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", closeCts.Token).ConfigureAwait(false); }
            catch { /* a courtesy close: a peer that cannot take it is closed regardless */ }
        }
    }

    /// <summary>Whether a frame left the connection usable.</summary>
    internal enum FrameStatus
    {
        /// <summary>Handled (or ignored). Keep reading.</summary>
        Handled,

        /// <summary>A safety cap tripped; this connection has to end, and <c>Reason</c> says which.</summary>
        Refused,
    }

    /// <summary>The outcome of one inbound frame.</summary>
    internal readonly record struct FrameOutcome(FrameStatus Status, string? Reason);

    /// <summary>
    ///     Handles one inbound frame for an attached session: a navigation, a JS round-trip reply, a .NET
    ///     invocation, or an event handler dispatched in arrival order.
    /// </summary>
    /// <remarks>
    ///     Written against the session rather than a socket, so every transport runs the same protocol. The
    ///     backpressure breaker RETURNS rather than closing anything: a WebSocket ends with a close frame, a
    ///     Server-Sent Events stream by completing its response, and a POST by answering — which of those to
    ///     do is the caller's business, not this method's.
    /// </remarks>
    internal static async ValueTask<FrameOutcome> ProcessFrameAsync(
        LiveSession session,
        JsonElement root,
        bool hasType,
        JsonElement type,
        int payloadLength,
        LiveSessionStore store,
        RaskServerLimits limits,
        RaskMetrics? metrics,
        CancellationToken ct)
    {
        if (session.SuppressEventsUntilReconnect)
        {
            // Auth handoff in flight: a redeem fetch + reconnect are happening on the client. Drop
            // everything except a future hello (handled before this).
            return new FrameOutcome(FrameStatus.Handled, null);
        }

        if (hasType && type.ValueEquals("navigate"u8))
        {
            await HandleNavigateAsync(session, root, ct).ConfigureAwait(false);
            return new FrameOutcome(FrameStatus.Handled, null);
        }

        if (hasType && HandledInline(session, root, type))
        {
            return new FrameOutcome(FrameStatus.Handled, null);
        }

        var handlerId = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString()
            : null;
        if (handlerId is null)
        {
            return new FrameOutcome(FrameStatus.Handled, null);
        }

        // Dispatch the handler in ARRIVAL order while keeping the caller's reader alive, so async handlers can
        // interleave with the jsResult / dotNetInvoke frames they are awaiting (those paths run inline above —
        // never through DispatchHandlerAsync). The chain is rebuilt per message: capture the prior tail, assign
        // a new continuation that awaits it before dispatching, and store that as the next tail.
        //
        // Why not Task.Run + session.Lock.WaitAsync (the prior shape): SemaphoreSlim is FIFO based on the order
        // callers invoke WaitAsync, not the order Task.Run was invoked. Under ThreadPool contention, two
        // messages spawned input→submit can race and acquire the lock submit→input — letting submit handlers
        // read a stale EditContext that the preceding input handler had not applied yet. The async chaining
        // below pins start-of-dispatch order to arrival order without blocking the reader.
        var payloadBytes = (long)payloadLength;
        if (!AdmitHandler(session, store, limits, metrics, payloadBytes))
        {
            return new FrameOutcome(FrameStatus.Refused, "handler backlog");
        }

        // We clone the JSON element because the JsonDocument's backing buffer is disposed by the caller once
        // this frame is handled.
        var capturedRoot = root.Clone();
        session.EnqueueOnHandlerChain(previous => ChainHandlerDispatchAsync(
            previous,
            store,
            session,
            handlerId,
            capturedRoot,
            payloadBytes,
            metrics,
            limits.HandlerTimeout,
            ct));

        return new FrameOutcome(FrameStatus.Handled, null);
    }

    // The frames answered on the reader itself, without a render.
    private static bool HandledInline(LiveSession session, JsonElement root, JsonElement type)
    {
        if (type.ValueEquals("jsResult"u8))
        {
            // Round-trip reply for an IJSRuntime.InvokeAsync<T> call. The base JSRuntime class manages its
            // own pending-task dictionary keyed by the taskId we passed out in jsInvokes; calling EndInvokeJS
            // with the serialised [taskId, success, result|error] triple completes the awaiting ValueTask. No
            // render needed.
            HandleJsResult(session, root);
            return true;
        }

        if (type.ValueEquals("dotNetInvoke"u8))
        {
            // JS-side DotNet.invokeMethodAsync calling into a [JSInvokable] method. Hand off to the public
            // DotNetDispatcher; the runtime completes the call asynchronously and EndInvokeDotNet fires our
            // SendOutOfBandAsync to deliver the result back to the client. No render needed.
            HandleDotNetInvoke(session, root);
            return true;
        }

        return false;
    }

    // Backpressure circuit-breaker: bound both the number of dispatches queued on the chain
    // (MaxPendingHandlers) and their aggregate cloned-payload bytes (MaxPendingHandlerBytes). When handlers
    // drain slower than the client sends (a flood) or the chain head is stuck (a hung handler), the queue —
    // each entry holding a cloned JsonElement — would grow without limit. Trip BEFORE cloning, so the
    // payload that would be dropped is never allocated.
    private static bool AdmitHandler(
        LiveSession session, LiveSessionStore store, RaskServerLimits limits, RaskMetrics? metrics, long payloadBytes)
    {
        var pending = session.IncrementPendingHandlers();
        store.HandlerQueued();
        var pendingBytes = session.AddPendingHandlerBytes(payloadBytes);
        if ((limits.MaxPendingHandlers > 0 && pending > limits.MaxPendingHandlers)
            || (limits.MaxPendingHandlerBytes > 0 && pendingBytes > limits.MaxPendingHandlerBytes))
        {
            session.DecrementPendingHandlers();
            store.HandlerDequeued();
            session.SubtractPendingHandlerBytes(payloadBytes);
            metrics?.FrameRejected("backlog");
            return false;
        }

        return true;
    }

    /// <summary>What a <c>hello</c> did.</summary>
    internal enum AttachStatus
    {
        /// <summary>The frame named no session. Keep reading: nothing was asked for, so nothing is refused.</summary>
        Ignored,

        /// <summary>The connection is attached to a session and has been counted.</summary>
        Attached,

        /// <summary>
        ///     No session to attach to: this host never had that id, or the sender does not own it. The
        ///     caller answers the unknown-session frame and ends the connection — the same answer either
        ///     way, so a prober cannot tell the two apart (#1075).
        /// </summary>
        Unknown,
    }

    /// <summary>The session this connection is now driving.</summary>
    internal readonly record struct AttachOutcome(AttachStatus Status, LiveSession? Session);

    /// <summary>
    ///     Attaches a connection to the session its <c>hello</c> names, resuming one this host never had
    ///     when the client carries a record for it.
    /// </summary>
    /// <remarks>
    ///     Written against <see cref="ILiveTransport" /> rather than a WebSocket, because every transport
    ///     opens the same way: a client names a session, proves it may drive it, and gets a catch-up render
    ///     for whatever it missed. The caller keeps the answer to a refusal, since how "the connection ends"
    ///     is the transport's business.
    /// </remarks>
    internal static async Task<AttachOutcome> AttachAsync(
        string? sessionId,
        string? resumeToken,
        ILiveTransport transport,
        LiveSessionStore store,
        RaskServerLimits limits,
        ClaimsPrincipal user,
        SessionResumeSupport resume,
        RaskRootSelector selector,
        RaskMetrics? metrics,
        CultureNegotiation resumeCulture,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return new AttachOutcome(AttachStatus.Ignored, null);
        }

        // A session that is not this principal's is treated exactly as one that does not exist — resume record
        // and all — so a leaked id tells a stranger nothing, not even that it is live (#1075). Peek, not Get:
        // Get cancels the session's pending removal, which a refused hello must not be able to do. It is
        // cancelled below, once this connection has actually attached.
        var session = store.Peek(sessionId) is { } existing && MayAttach(user, existing) ? existing : null;
        if (session is null)
        {
            return await ResumeAttachAsync(
                resumeToken, transport, store, limits, user, resume, selector, metrics, resumeCulture, ct)
                .ConfigureAwait(false);
        }

        // Counted here rather than inside AttachTransport: the store owns the number, and the session has no
        // reason to know a store exists. A connection that replaces one still attached is not counted again;
        // that one's cleanup will not count its detach.
        if (session.AttachTransport(transport, ct))
        {
            store.SocketAttached();
        }

        // After publishing, not before: a stale connection's cleanup that ran in between could otherwise arm a
        // removal nothing cancels. (The removal also skips a session with an open connection when it fires.)
        store.CancelPendingRemoval(session.Id);
        session.Leaving = false;
        session.Services.GetRequiredService<SessionUserProvider>().Set(user);

        try
        {
            await RenderAttachedAsync(session, ct).ConfigureAwait(false);
        }
        catch
        {
            AbandonAttach(session, transport, store, limits);
            throw;
        }

        return new AttachOutcome(AttachStatus.Attached, session);
    }

    // What a re-attached session owes its new connection: the deferred sign-in navigation, then a render.
    private static async Task RenderAttachedAsync(LiveSession session, CancellationToken ct)
    {
        var routeState = session.Services.GetRequiredService<RouteState>();
        var destination = session.PendingAuthNavigation is { } url
            ? SplitUrl(url)
            : (routeState.Path, routeState.Query);

        // Before the route moves: this is the render the sign-in handoff deferred its route check to, and a
        // returnUrl is only known to be local, not to be a page this principal may see.
        var guard = await GuardRouteAsync(session, destination.Path, destination.Query).ConfigureAwait(false);
        if (guard is { } redirect)
        {
            session.PendingAuthNavigation = null;
            await RenderGuardRedirectAsync(session, redirect, ct).ConfigureAwait(false);
            return;
        }

        // Apply a deferred sign-in/out navigation now that the principal is re-seeded, so the destination
        // page mounts fresh under the new identity (its Mount runs against the redeemed principal).
        // The attach flagged a pending render for this reconnect, so the flush below performs a real render
        // against the updated route. See LiveSession.PendingAuthNavigation.
        if (session.PendingAuthNavigation is not null)
        {
            session.PendingAuthNavigation = null;
            routeState.Path = destination.Path;
            routeState.Query = destination.Query;

            // A destination another application owns loads as a page instead (#1094).
            if (await NavigateAcrossApplicationsAsync(session, replace: true).ConfigureAwait(false))
            {
                return;
            }
        }

        // Only emit a catch-up render when something asked to render during the GET-to-hello handoff window
        // (or while detached across a reconnect). When no drop happened, the browser's HTML still reflects
        // the session state and re-rendering would just re-fire OnRendered on every alive component for no
        // visible change — that's what made Server's initial-mount hook count diverge from WASM's.
        // FlushPendingRenderAsync is a no-op when nothing's pending.
        await session.FlushPendingRenderAsync().ConfigureAwait(false);
    }

    // Moves the session to the guard's redirect and sends it as ONE frame carrying the new address. Inside a
    // handler scope, because moving the route asks for a render of its own: outside one that render goes out
    // first, and the frame with the address is then dropped as a duplicate of it.
    private static async Task RenderGuardRedirectAsync(LiveSession session, GuardRedirect redirect, CancellationToken ct)
    {
        await session.Lock.WaitAsync(ct).ConfigureAwait(false);
        session.InHandlerScope = true;
        try
        {
            redirect.ApplyTo(session.Services.GetRequiredService<RouteState>());
            await session.RenderAndSendCoalescingAsync(redirect.Url, true).ConfigureAwait(false);
        }
        finally
        {
            session.InHandlerScope = false;
            session.Lock.Release();
            _ = session.DrainRenderRequestedAfterScope();
        }
    }

    private static async Task<AttachOutcome> ResumeAttachAsync(
        string? resumeToken,
        ILiveTransport transport,
        LiveSessionStore store,
        RaskServerLimits limits,
        ClaimsPrincipal user,
        SessionResumeSupport resume,
        RaskRootSelector selector,
        RaskMetrics? metrics,
        CultureNegotiation resumeCulture,
        CancellationToken ct)
    {
        // This host has never heard of the session. Before the resume protocol that was the end of it —
        // the client reloaded and the user lost their page. If the client carries a record we can open,
        // rebuild the page around it instead.
        var session = string.IsNullOrEmpty(resumeToken)
            ? null
            : TryResumeSession(resumeToken, user, resume, store, metrics, selector);

        if (session is null)
        {
            return new AttachOutcome(AttachStatus.Unknown, null);
        }

        // The rebuilt session has a NEW id. The client learns it from the full frame below, which re-stamps
        // data-rask-root — see LiveSessionBase's full-payload path. Counted like any other attach: the
        // cleanup counts the detach, so an uncounted attach here drove ConnectedCount negative after every
        // deploy (#1059).
        if (session.AttachTransport(transport, ct))
        {
            store.SocketAttached();
        }

        session.Services.GetRequiredService<SessionUserProvider>().Set(user);

        // A resumed session is a NEW DI scope, so its culture starts at the app default. Without this the
        // visitor's language would silently reset the first time the host restarted under them — the one
        // moment resume exists to hide.
        if (resumeCulture.Culture is not null)
        {
            ServerCultureNegotiation.Apply(session.Services, resumeCulture);
        }

        try
        {
            // The record names a URL and a user, not what that user may see today: a role removed since the
            // record was sealed must not get the page back by resuming it. Nothing is mounted yet, so moving
            // the route here asks nobody for a render.
            var guardUrl = await RedirectUnauthorizedRouteAsync(session).ConfigureAwait(false);
            await session.RenderAndSendAsync(guardUrl, guardUrl is not null).ConfigureAwait(false);
        }
        catch
        {
            AbandonAttach(session, transport, store, limits);
            throw;
        }

        return new AttachOutcome(AttachStatus.Attached, session);
    }

    // The attach's render threw — most often the client dropping mid-attach. The caller never learns this
    // connection attached (no outcome is returned), so its cleanup cannot undo it: this does, or the session keeps
    // a dead transport with no removal armed and the connected count never comes back down.
    private static void AbandonAttach(
        LiveSession session, ILiveTransport transport, LiveSessionStore store, RaskServerLimits limits)
    {
        if (session.DetachTransport(transport))
        {
            store.SocketDetached();
            store.ScheduleRemoval(session.Id, limits.SessionGracePeriod);
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
        // Formatted straight to UTF-8 in a rented buffer — the send is awaited before it goes back.
        var buffer = ArrayPool<byte>.Shared.Rent(64);
        try
        {
            await session.SendOutOfBandAsync(buffer.AsMemory(0, WriteHandlerAck(buffer, seq))).ConfigureAwait(false);
        }
        catch
        {
            // Swallow: the client re-syncs on the next ack or its hard-timeout backstop.
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // {"type":"ack","seq":N} — 41 bytes at most (long.MinValue).
    internal static int WriteHandlerAck(Span<byte> destination, long seq)
    {
        System.Text.Unicode.Utf8.TryWrite(destination, CultureInfo.InvariantCulture,
            $"{{\"type\":\"ack\",\"seq\":{seq}}}", out var written);
        return written;
    }

    private static Task DispatchHandlerAsync(
        LiveSession session,
        string handlerId,
        JsonElement root,
        RaskMetrics? metrics,
        TimeSpan handlerTimeout,
        CancellationToken ct) =>
        RunInSessionAsync(
            session,
            "rask.handler.dispatch",
            handlerId,
            token => session.View.TryInvokeHandlerAsync(handlerId, root, session.Services, token),
            metrics,
            handlerTimeout,
            ct);

    /// <summary>
    ///     The body every dispatch into a session shares: take the session's lock, hold the handler scope so state changes
    ///     coalesce into one render, re-check the route's authorization, run <paramref name="invoke" />, and render with
    ///     whatever navigation or sign-in it asked for.
    /// </summary>
    private static async Task RunInSessionAsync(
        LiveSession session,
        string activityName,
        string? handlerId,
        Func<CancellationToken, ValueTask<bool>> invoke,
        RaskMetrics? metrics,
        TimeSpan handlerTimeout,
        CancellationToken ct)
    {
        if (session.IsDisposed)
        {
            return;
        }

        try
        {
            await session.Lock.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }

        session.InHandlerScope = true;

        // The one gate every handler passes through, under the session lock —
        // so the session's services are ambient for exactly the work, and released when it ends.
        using var work = session.EnterWorkScope();
        using var activity = RaskActivity.Source.StartActivity(activityName);
        if (handlerId is not null)
        {
            activity?.SetTag("rask.handler.id", handlerId);
        }

        metrics?.HandlerDispatched();
        var dispatchStart = Stopwatch.GetTimestamp();
        using var handlerCts = HandlerTimeoutSource(handlerTimeout, ct);
        try
        {
            var navigator = session.Services.GetRequiredService<Navigator>();
            var authSignIn = session.Services.GetRequiredService<AuthSignIn>();
            var ticketStore = session.Services.GetRequiredService<IAuthTicketStore>();
            try
            {
                await InvokeHandlerAsync(
                    session, navigator, authSignIn, ticketStore, invoke, handlerCts?.Token ?? default, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (handlerCts is not null && handlerCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // The handler timed out and observed CancellationToken, unwinding cleanly. The
                // session survives; record it rather than letting it look like a normal cancellation.
                ReportHandlerTimedOut(metrics, activity, handlerId ?? activityName, handlerTimeout);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ReportHandlerFaulted(session, metrics, activity, handlerId ?? activityName, ex);
            }
        }
        finally
        {
            session.InHandlerScope = false;
            ReleaseDispatchLock(session);
            _ = session.DrainRenderRequestedAfterScope();
            metrics?.RecordHandlerDuration(Stopwatch.GetElapsedTime(dispatchStart).TotalMilliseconds);
        }
    }

    private static void ReleaseDispatchLock(LiveSession session)
    {
        try
        {
            session.Lock.Release();
        }
        catch (ObjectDisposedException)
        {
            // Disposing a session disposes this lock while the handler that holds it is still running. By
            // then nobody is left to wait on it, so there is nothing to hand it back to.
        }
    }

    // Action timeout: cancel the dispatch's CancellationToken after handlerTimeout (linked to
    // the socket so a close cancels it too). A handler that threads CancellationToken into its
    // async work unwinds cooperatively; one that ignores it can't be force-aborted (the timeout is
    // still logged + metered). Null when the timeout is disabled, so the default path allocates nothing.
    private static CancellationTokenSource? HandlerTimeoutSource(TimeSpan handlerTimeout, CancellationToken ct)
    {
        if (handlerTimeout <= TimeSpan.Zero)
        {
            return null;
        }

        var handlerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        handlerCts.CancelAfter(handlerTimeout);
        return handlerCts;
    }

    /// <summary>Runs one handler inside the navigator's and the sign-in's handler scopes, then renders.</summary>
    /// <remarks>
    ///     Called once per dispatched event, so its state machine comes from a pool rather than the heap.
    /// </remarks>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private static async ValueTask InvokeHandlerAsync(
        LiveSession session,
        Navigator navigator,
        AuthSignIn authSignIn,
        IAuthTicketStore ticketStore,
        Func<CancellationToken, ValueTask<bool>> invoke,
        CancellationToken dispatchToken,
        CancellationToken ct)
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
            await RevalidateUserAsync(session, ct).ConfigureAwait(false);

            if (!await IsCurrentRouteAuthorizedAsync(session).ConfigureAwait(false))
            {
                await EnforceAuthAndRenderAsync(session, null, false).ConfigureAwait(false);
                return;
            }

            if (!await invoke(dispatchToken).ConfigureAwait(false))
            {
                return;
            }

            var authInstruction = TakeNavigation(
                session, navigator, authSignIn, ticketStore, out var historyUrl, out var historyReplace);

            await EnforceAuthAndRenderAsync(
                    session, historyUrl, historyReplace, authInstruction)
                .ConfigureAwait(false);

            if (authInstruction is not null)
            {
                session.SuppressEventsUntilReconnect = true;
            }
        }
    }

    // Where the handler asked to go: a sign-in/out it started, or else a NavigateTo.
    private static AuthInstruction? TakeNavigation(
        LiveSession session,
        Navigator navigator,
        AuthSignIn authSignIn,
        IAuthTicketStore ticketStore,
        out string? historyUrl,
        out bool historyReplace)
    {
        historyUrl = null;
        historyReplace = false;
        AuthInstruction? authInstruction = null;

        if (authSignIn.TryConsume(out var pending))
        {
            var safeReturn = SanitizeReturnUrl(pending.ReturnUrl);
            var ticketId = ticketStore.Issue(
                pending.Action,
                pending.Principal,
                pending.Scheme,
                session.Id,
                pending.Persistent);
            authInstruction = new AuthInstruction(ticketId, safeReturn);

            // Do NOT navigate routeState here. Setting it to the destination now would
            // mount the destination page under the PRE-SignIn principal — SessionUserProvider
            // is only re-seeded on the reconnect handshake — so its Mount would load
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

        if (navigator.TryConsumeHistory(out var url, out var replace) && authInstruction is null)
        {
            historyUrl = url;
            historyReplace = replace;
        }

        return authInstruction;
    }

    private static void ReportHandlerTimedOut(RaskMetrics? metrics, Activity? activity, string handler, TimeSpan handlerTimeout)
    {
        metrics?.HandlerTimedOut();
        activity?.SetStatus(ActivityStatusCode.Error, "handler timed out");
        RaskDiagnostics.Report(
            RaskLogLevel.Warning, "Rask.Live",
            $"Rask Live handler '{handler}' cancelled after HandlerTimeout ({handlerTimeout})");
    }

    private static void ReportHandlerFaulted(
        LiveSession session, RaskMetrics? metrics, Activity? activity, string handler, Exception ex)
    {
        // A shutdown that outlasts its drain budget disposes a session whose handler is still running. The
        // handler then returns into a session with no services left to render with: it did not throw, and there
        // is nobody to send a render to.
        if (ex is ObjectDisposedException && session.IsDisposed)
        {
            return;
        }

        metrics?.HandlerFaulted();
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        RaskDiagnostics.Report(RaskLogLevel.Error, "Rask.Live", $"Rask Live handler '{handler}' threw", ex);
    }

    private static async Task SendSessionUnknownAsync(WebSocket ws, CancellationToken ct)
    {
        try
        {
            await ws.SendAsync(SessionUnknownPayload, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        catch
        {
            // Best effort: a client that cannot hear "session unknown" reconnects from scratch anyway.
        }
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
        var (path, query) = SplitUrl(record.Url);

        // A devtools panel is opened per request, never rebuilt from a record: the reload this forces is a GET, and the
        // GET asks who may open it.
        if (!selector.CanResume(path))
        {
            metrics?.ResumeRejected("devtools");
            return null;
        }

        var session = store.TryCreate(selector.FactoryFor(path));
        if (session is null)
        {
            metrics?.ResumeRejected("atcapacity");
            return null;
        }

        // Seed the route and the declared state BEFORE the first render, so the page builds against them
        // rather than rendering a default and then correcting itself in a second frame the user would see.
        BindToApplication(session, selector, path);
        var routeState = session.Services.GetRequiredService<RouteState>();
        routeState.Path = path;
        routeState.Query = query;
        session.Services.GetRequiredService<PersistentState>().Restore(record.Entries);

        metrics?.SessionResumed();
        return session;
    }


    // Ties a new session to the application on this host that owns the path it was opened at (#1094): its live
    // navigations and its Router resolve against that application's route table, and a navigation to a path
    // another application owns becomes a real page load (see NavigateAcrossApplicationsAsync). Both ways a
    // session is born — the GET and a resume — come through here, so they cannot disagree.
    private static void BindToApplication(LiveSession session, RaskRootSelector selector, string path)
    {
        session.Services.GetRequiredService<RouteState>().Table = selector.TableFor(path);
        session.OwnsPath = other => selector.SameApplication(path, other);
    }

    // When the session's route has moved to a path another application on this host owns, tells the client to
    // load that URL as a page, so its GET builds the root that application renders with. Returns whether it did.
    // Rendering the path in place would show another application's pages inside this one's document — or, now
    // that the table is scoped, this application's not-found page for a URL that does exist.
    private static async Task<bool> NavigateAcrossApplicationsAsync(LiveSession session, bool replace)
    {
        var routeState = session.Services.GetRequiredService<RouteState>();
        if (session.OwnsPath is not { } owns || owns(routeState.Path))
        {
            return false;
        }

        // The path came from the client's navigate frame or the app's own navigation; either way it must stay on
        // this origin once the client hands it to location.
        var url = LocalUrl.Sanitize(QueryString.Build(routeState.Path, routeState.Query));
        await session.SendOutOfBandAsync(LocationFrame(url, replace, outside: false)).ConfigureAwait(false);

        // The browser is leaving this page. The route change asked for a render in scope; drained after the dispatch, it
        // would paint the other application's URL into this one's tree on its way out.
        session.DiscardPendingRender();
        return true;
    }

    // Sends the browser to a page of this site the app does not render (Go.Out): the address as written, loaded
    // as a page. Nothing more is rendered for this one — the reader is on their way out of it.
    private static async Task<bool> LeaveTheApplicationAsync(LiveSession session, Navigator navigator)
    {
        if (!navigator.TryConsumeExit(out var exit))
        {
            return false;
        }

        await session.SendOutOfBandAsync(LocationFrame(LocalUrl.Sanitize(exit), replace: false, outside: true))
            .ConfigureAwait(false);
        session.DiscardPendingRender();
        return true;
    }

    private static byte[] LocationFrame(string url, bool replace, bool outside)
    {
        var buffer = new ArrayBufferWriter<byte>(64 + url.Length);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type"u8, "location"u8);
            writer.WriteString("url"u8, url);
            writer.WriteBoolean("replace"u8, replace);
            if (outside)
            {
                writer.WriteBoolean("outside"u8, true);
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
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

    // A page is never framed by another site (clickjacking a signed-in user into a click they did not mean) and
    // never sniffed into another type. Added only where the app has not set its own, so middleware that runs
    // earlier — an app meant to be embedded, a full CSP — keeps the last word.
    internal static void ApplyPageSecurityHeaders(IHeaderDictionary headers)
    {
        headers.TryAdd("X-Frame-Options", "SAMEORIGIN");
        headers.TryAdd("Content-Security-Policy", "frame-ancestors 'self'");
        headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
        headers.TryAdd("X-Content-Type-Options", "nosniff");
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
            // Names this session as the caller, so a framework callback registry answers only ids its own
            // session registered — a static [JSInvokable] is otherwise reachable from every socket.
            using var caller = JsCaller.Enter(runtime);
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

        var fullUrl = navPath;
        if (!string.IsNullOrEmpty(navQueryString))
        {
            fullUrl += navQueryString.StartsWith('?') ? navQueryString : "?" + navQueryString;
        }

        await session.Lock.WaitAsync(ct).ConfigureAwait(false);
        session.InHandlerScope = true;
        try
        {
            var routeState = session.Services.GetRequiredService<RouteState>();
            routeState.Path = navPath;
            routeState.Query = QueryString.Parse(navQueryString);

            try
            {
                // A navigation mounts a page just as a handler does, so an ended sign-in must stop it too.
                await RevalidateUserAsync(session, ct).ConfigureAwait(false);

                // And the page it mounts may send the reader on, as it may from a handler or the first request.
                using var navigating = session.Services.GetRequiredService<Navigator>().EnterHandler();
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
            _ = session.DrainRenderRequestedAfterScope();
        }
    }

    /// <summary>
    ///     Makes the navigation a lifecycle hook asked for when no dispatch was waiting for it: the route moves under
    ///     the session's lock, and the destination is rendered behind its guard in place of the page that asked.
    /// </summary>
    internal static async Task NavigateFromHookAsync(LiveSession session, Action navigate)
    {
        if (session.IsDisposed)
        {
            return;
        }

        try
        {
            await session.Lock.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        session.InHandlerScope = true;
        try
        {
            using var work = session.EnterWorkScope();
            var navigator = session.Services.GetRequiredService<Navigator>();
            using var navigating = navigator.EnterHandler();
            await RevalidateUserAsync(session, CancellationToken.None).ConfigureAwait(false);
            navigate();
            if (navigator.TryConsumeHistory(out var url, out _))
            {
                await EnforceAuthAndRenderAsync(session, url, replace: true).ConfigureAwait(false);
            }
            else
            {
                await LeaveTheApplicationAsync(session, navigator).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !session.IsDisposed)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Error, "Rask.Live", "Rask Live navigation from a lifecycle hook threw", ex);
        }
        finally
        {
            session.InHandlerScope = false;
            ReleaseDispatchLock(session);
            _ = session.DrainRenderRequestedAfterScope();
        }
    }

    // Evaluates the route guard for the session's current route + principal. Returns true when the
    // route resolves and the guard allows it, or when no route resolves (nothing to gate — e.g. a
    // NotFound page); false when the guard would challenge/forbid. Used to gate handler dispatch.
    // How often a live session's principal is re-checked against its sign-in before a dispatch.
    internal static readonly TimeSpan RevalidateUserEvery = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Re-checks the session's principal against its sign-in, at most every <see cref="RevalidateUserEvery" />.
    /// </summary>
    /// <remarks>
    ///     A page open for hours holds the principal it attached with. When the sign-in behind it has ended (signed out
    ///     on another device, a password reset) the principal is cleared, and the route re-check that follows sends the
    ///     challenge. When the claims changed (a role granted or removed) the principal is replaced, so the page re-renders
    ///     under the new ones. Nothing is checked when the app registered no <see cref="ISessionRevalidator" />.
    /// </remarks>
    private static async Task RevalidateUserAsync(LiveSession session, CancellationToken cancellationToken)
    {
        if (session.Services.GetService<ISessionRevalidator>() is not { } revalidator)
        {
            return;
        }

        var users = session.Services.GetRequiredService<SessionUserProvider>();
        if (users.Current.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (session.LastUserRevalidation != 0
            && now - session.LastUserRevalidation < (long)RevalidateUserEvery.TotalMilliseconds)
        {
            return;
        }

        session.LastUserRevalidation = now;

        var fresh = await revalidator.Revalidate(users.Current, cancellationToken).ConfigureAwait(false);
        if (fresh is null)
        {
            users.Clear();
        }
        else if (!SameClaims(users.Current, fresh))
        {
            users.Set(fresh);
        }
    }

    private static bool SameClaims(ClaimsPrincipal left, ClaimsPrincipal right) =>
        left.Claims.Select(static c => c.Type + "\u0000" + c.Value).Order(StringComparer.Ordinal)
            .SequenceEqual(right.Claims.Select(static c => c.Type + "\u0000" + c.Value).Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static async Task<bool> IsCurrentRouteAuthorizedAsync(LiveSession session)
    {
        var routeState = session.Services.GetRequiredService<RouteState>();
        if (!session.Routes.TryResolve(routeState.CurrentTable, routeState.Path, out var chain))
        {
            return true;
        }

        var user = session.Services.GetRequiredService<SessionUserProvider>().Current;
        var result = await RouteAuthorizationGuard
            .Evaluate(session.Services, chain, user)
            .ConfigureAwait(false);
        return result.Outcome == RouteAuthorizationOutcome.Allow;
    }

    private static async Task EnforceAuthAndRenderAsync(
        LiveSession session,
        string? historyUrl,
        bool replace,
        AuthInstruction? auth = null)
    {
        var navigator = session.Services.GetRequiredService<Navigator>();
        if (auth is null && await LeaveTheApplicationAsync(session, navigator).ConfigureAwait(false))
        {
            return;
        }

        // When emitting an auth instruction, skip route auth re-eval: the cookie hasn't
        // landed yet on this WS, so the SessionUserProvider still holds the pre-SignIn
        // principal. The post-reconnect render does the real check with the new identity.
        if (auth is null)
        {
            // A path another application on this host owns is not this session's to render at all (#1094).
            if (await NavigateAcrossApplicationsAsync(session, replace).ConfigureAwait(false))
            {
                return;
            }

            if (await RedirectUnauthorizedRouteAsync(session).ConfigureAwait(false) is { } guardUrl)
            {
                historyUrl = guardUrl;
                replace = true;
            }
        }

        await session.RenderAndSendCoalescingAsync(historyUrl, replace, auth).ConfigureAwait(false);

        // A page that navigated as it mounted had its frame withheld: its destination is rendered instead, under
        // that route's own guard, and is the address the reader gets — the live form of the first request's 302.
        if (auth is null && await LeaveTheApplicationAsync(session, navigator).ConfigureAwait(false))
        {
            return;
        }

        if (auth is null && navigator.TryConsumeRedirect(out var redirectUrl, out var redirectReplace))
        {
            await EnforceAuthAndRenderAsync(session, redirectUrl, historyUrl is null ? redirectReplace : replace)
                .ConfigureAwait(false);
        }
    }

    // Points the session's route at the guard's redirect when its principal may not see the page it is on,
    // and returns the URL the address bar should then show; null when the page is allowed.
    private static async Task<string?> RedirectUnauthorizedRouteAsync(LiveSession session)
    {
        var routeState = session.Services.GetRequiredService<RouteState>();
        if (await GuardRouteAsync(session, routeState.Path, routeState.Query).ConfigureAwait(false) is not { } redirect)
        {
            return null;
        }

        redirect.ApplyTo(routeState);
        return redirect.Url;
    }

    // Where the guard sends the session's principal instead of a route; null when it may see the page (or the
    // path resolves to nothing — a NotFound page has nothing to gate). Changes nothing: moving the route asks
    // for a render, so a caller has to know the answer before it moves it.
    private static async Task<GuardRedirect?> GuardRouteAsync(
        LiveSession session, string path, Rask.Core.Routing.IQueryCollection query)
    {
        var routeState = session.Services.GetRequiredService<RouteState>();
        if (!session.Routes.TryResolve(routeState.CurrentTable, path, out var chain))
        {
            return null;
        }

        var user = session.Services.GetRequiredService<SessionUserProvider>().Current;
        var result = await RouteAuthorizationGuard.Evaluate(session.Services, chain, user).ConfigureAwait(false);

        // Client-side guard redirect targets. (The initial HTTP GET challenge already goes
        // through the configured auth scheme's own LoginPath/AccessDeniedPath.)
        return result.Outcome switch
        {
            RouteAuthorizationOutcome.Allow => null,
            RouteAuthorizationOutcome.Forbid => new GuardRedirect(RouteAuthorizationGuard.ForbidPath, string.Empty),
            _ => new GuardRedirect(
                RouteAuthorizationGuard.ChallengePath,
                "?returnUrl=" + Uri.EscapeDataString(QueryString.Build(path, query))),
        };
    }

    private readonly record struct GuardRedirect(string Path, string Query)
    {
        public string Url => Path + Query;

        public void ApplyTo(RouteState routeState)
        {
            routeState.Path = Path;
            routeState.Query = Query.Length == 0 ? QueryCollection.Empty : QueryString.Parse(Query);
        }
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

        var (ticketId, sessionId) = await ReadRedeemRequestAsync(ctx).ConfigureAwait(false);
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
            // Persistence travels with the ticket: a "remember me" sign-in from a component gets the same
            // browser-outliving cookie the /api/auth login endpoint writes.
            await ctx.SignInAsync(
                    scheme,
                    ticket.Principal!,
                    new AuthenticationProperties { IsPersistent = ticket.Persistent })
                .ConfigureAwait(false);
        }
        else
        {
            await ctx.SignOutAsync(scheme).ConfigureAwait(false);
        }

        // The reconnect that follows carries this principal, not the session's owner, so let the hello
        // admission check expect it (MayAttach). Opened here rather than when the handler issued the
        // ticket: the client reconnects only once this response arrives, and tying the window to the
        // redeem means only whoever holds the ticket can open it. A sign-out opened at issue time let
        // any anonymous holder of the session id attach before the client had even redeemed (#1075).
        if (ctx.RequestServices.GetService<LiveSessionStore>()?.Peek(ticket.SessionId) is { } handoffSession)
        {
            handoffSession.PendingAuthHandoff = ticket.Principal ?? new ClaimsPrincipal(new ClaimsIdentity());
        }

        ctx.Response.StatusCode = StatusCodes.Status200OK;
    }

    // The ticket and the session it was issued for; both null when the body is not JSON.
    private static async Task<(string? TicketId, string? SessionId)> ReadRedeemRequestAsync(HttpContext ctx)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted).ConfigureAwait(false);
            return (StringProperty(doc.RootElement, "ticket"), StringProperty(doc.RootElement, "session"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    // True when the request carries no Origin/Referer (same-origin fetches may omit Origin — the
    // ticket secrecy covers those) or one whose host matches the request's own host. Host only, for the
    // reason SameOrigin gives; here the single-use session-bound ticket is the real authority (see
    // RedeemAuthTicketAsync), so a host-only check is the right belt-and-braces. The rule itself is
    // shared with Rask.Signaling, which cannot reference this assembly.
    internal static bool IsSameOrigin(HttpRequest request) => SameOrigin.Allows(request);

    // Binds an id-addressed endpoint (upload / download) to the principal that owns the live
    // session. The {sessionId} in the URL is the only thing tying the request to a session, so a
    // leaked id must not let a *different* signed-in user drive a victim's session. An anonymous
    // session is matched by anyone — the unguessable sessionId is the only authority then (the same
    // posture the WS handshake takes); an authenticated session requires the request to carry the
    // same authenticated identity.
    //
    // A signed-in principal with neither a NameIdentifier nor a Name claim has nothing to compare, and matches nobody
    // (#1102). Comparing two such keys used to compare null with null, which is equal, so ANY keyless user passed as
    // the owner of ANY other keyless user's session. Refused instead, and said once, because the app's sign-in is
    // what has to change: a session whose owner carries no key cannot be reattached even by its owner, so its tab
    // reloads on every reconnect until the principal gets one.
    internal static bool SameSessionUser(ClaimsPrincipal request, ClaimsPrincipal owner)
    {
        if (owner.Identity?.IsAuthenticated != true)
        {
            return true;
        }

        if (request.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (UserKey(owner) is not { } ownerKey || UserKey(request) is not { } requestKey)
        {
            ReportKeylessPrincipalOnce();
            return false;
        }

        return string.Equals(requestKey, ownerKey, StringComparison.Ordinal);
    }

    private static int _keylessPrincipalReported;

    private static void ReportKeylessPrincipalOnce()
    {
        if (Interlocked.Exchange(ref _keylessPrincipalReported, 1) == 0)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Warning, "Rask.Live",
                "A signed-in user has neither a NameIdentifier nor a Name claim, so Rask cannot tell whether a "
                + "reconnect, an upload or a download comes from the same user, and refuses it: the page reloads "
                + "on every reconnect. Add a ClaimTypes.NameIdentifier (or ClaimTypes.Name) claim when signing in.");
        }
    }

    /// <summary>Lets a test see the one-time keyless-principal warning again.</summary>
    internal static void ResetKeylessPrincipalReportForTests() => Interlocked.Exchange(ref _keylessPrincipalReported, 0);

    // Whether a hello's principal may attach to a session that already exists (#1075). The owner may,
    // under the same rule the upload and download endpoints apply. So may the one principal an auth
    // handoff in flight is waiting for: that reconnect is the whole point of the handoff, and it arrives
    // as the redeemed identity (sign-in) or as nobody (sign-out) while the session still holds the old
    // one. Strict in both directions: a sign-out admits an anonymous reconnect, never some other user.
    internal static bool MayAttach(ClaimsPrincipal request, LiveSession session)
    {
        if (SameSessionUser(request, session.Services.GetRequiredService<SessionUserProvider>().Current))
        {
            return true;
        }

        return session.PendingAuthHandoff is { } expected
               && (expected.Identity?.IsAuthenticated == true
                   ? SameSessionUser(request, expected)
                   : request.Identity?.IsAuthenticated != true);
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
        return cleaned.Length == 0 || cleaned is "." or ".." ? "file" : cleaned;
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
            // A plain server app has nothing under _rask/a in its web root, so the miss is a 404. The
            // file is there when this process shares a host with a WebAssembly app — the operator
            // dashboard beside a bundle MapRaskSpa serves — because routing gives this endpoint the
            // bundle's /_rask/a/{hash} requests before static files run, and the bundle's hashes were
            // registered in the browser's runtime, never in this one.
            return ServeWebRootAssetAsync(ctx, hash, kind);
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
        var encoding = ContentEncodingNegotiation.Negotiate(ctx.Request);
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
    ///     Serves the scoped-script bundle's source map, which the bundle's last line names (#1073). Only a Debug
    ///     build's emit carries maps, so anywhere else — and for any hash that is not the current bundle — a 404.
    /// </summary>
    internal static Task ServeSourceMapAsync(HttpContext ctx)
    {
        var hash = ctx.Request.RouteValues["hash"] as string;
        if (!ScopedAssetBundle.IsContentHash(hash) || ScopedAssetRegistry.GetSourceMap(hash) is not { } map)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.Bytes(map.Utf8.ToArray(), "application/json; charset=utf-8", entityTag: new EntityTagHeaderValue(map.Etag))
            .ExecuteAsync(ctx);
    }

    /// <summary>
    ///     Serves a baked <c>_rask/a/{hash}.{ext}</c> file from the app's web root when this process's
    ///     registry doesn't carry the hash, with the same headers a registry hit gets.
    /// </summary>
    /// <remarks>
    ///     Read through <see cref="IWebHostEnvironment.WebRootFileProvider" /> rather than a directory, so
    ///     it finds the file wherever the web root is composed from — a published <c>wwwroot</c>, or a
    ///     bundle <c>MapRaskSpa</c> serves from elsewhere. The hash was validated as fixed-length hex
    ///     before it got here, so the path cannot leave <c>_rask/a/</c>.
    /// </remarks>
    private static async Task ServeWebRootAssetAsync(HttpContext ctx, string hash, AssetKind kind)
    {
        var files = ctx.RequestServices.GetService<IWebHostEnvironment>()?.WebRootFileProvider;
        var relative = "_rask/a/" + hash + ScopedAssetBundle.Extension(kind);
        if (files?.GetFileInfo(relative) is not { Exists: true } file)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers.Vary = "Accept-Encoding";
        ctx.Response.Headers.ETag = "\"" + hash + "\"";
        ctx.Response.ContentType = ScopedAssetBundle.ContentType(kind);

        // The publish bakes .br/.gz siblings next to each asset; one that matches the negotiated
        // encoding goes out verbatim, with no request-time CPU.
        var encoding = ContentEncodingNegotiation.Negotiate(ctx.Request);
        var suffix = encoding switch
        {
            "br" => ".br",
            "gzip" => ".gz",
            _ => null,
        };

        if (suffix is not null && files.GetFileInfo(relative + suffix) is { Exists: true } sibling)
        {
            ctx.Response.Headers.ContentEncoding = encoding;
            await ctx.Response.SendFileAsync(sibling, ctx.RequestAborted).ConfigureAwait(false);
            return;
        }

        await ctx.Response.SendFileAsync(file, ctx.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    ///     Whether a route template is already on this <see cref="IEndpointRouteBuilder" />.
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

    // ONE name for the two scripts, made from both: the runtime asks for the hooks under its own `?v=`, so that
    // has to move when either of them does, or a browser would keep last release's hooks for a year.
    private static readonly Lazy<string> RuntimeHash = new(
        () => RuntimeScript.HashOf(LoadEmbeddedScript("rask.js"), LoadEmbeddedScript("rask-hooks.js")));

    private static readonly Lazy<RuntimeScript> Runtime = new(
        () => new RuntimeScript(LoadEmbeddedScript("rask.js"), RuntimeHash.Value));

    private static readonly Lazy<RuntimeScript> Hooks = new(
        () => new RuntimeScript(LoadEmbeddedScript("rask-hooks.js"), RuntimeHash.Value));

    // Under the app's path base, which is the caller's to add: made once, because every first response asks.
    private static readonly Lazy<string> HooksUrl = new(() => HooksPath + "?v=" + RuntimeHash.Value);

    private static string LoadEmbeddedScript(string file)
    {
        var asm = typeof(RaskEndpointExtensions).Assembly;
        var name = asm.GetManifestResourceNames()
                       .FirstOrDefault(n => n.EndsWith(".Resources." + file, StringComparison.Ordinal))
                   ?? throw new InvalidOperationException(
                       $"The Rask client script is missing from {asm.GetName().Name} "
                       + $"{asm.GetName().Version}. This is a packaging fault rather than anything in "
                       + $"your app: the assembly should embed {file}. Clear obj/ and bin/ and rebuild; "
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
                   ?? throw new InvalidOperationException(
                       $"The Rask service worker is missing from {asm.GetName().Name} {asm.GetName().Version}. This is a "
                       + "packaging fault rather than anything in your app: clear obj/ and bin/ and rebuild; if it "
                       + "persists, reinstall the package.");
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
        // Peek, not Get: Get cancels the pending removal for good, pinning the session and all it staged.
        var session = sessions.Peek(sessionId);
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

            var entry = await StageFileAsync(ctx, uploads, sessionId, file, form, options).ConfigureAwait(false);
            if (entry is null)
            {
                RollbackStaged(uploads, staged);
                ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            staged.Add(entry);
        }

        await WriteStagedAsync(ctx, staged).ConfigureAwait(false);
    }

    private static async Task<SessionUploadStore.Entry?> StageFileAsync(
        HttpContext ctx, SessionUploadStore uploads, string sessionId, IFormFile file, IFormCollection form, RaskUploadOptions options)
    {
        long lastModified = 0;
        if (long.TryParse(form[$"{file.Name}__lastModified"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lm))
        {
            lastModified = lm;
        }

        // StageAsync atomically enforces the cumulative per-session quota: a null return means this
        // file would push the session over MaxBytesPerSession (the temp file is already cleaned up).
        return await uploads.StageAsync(
            sessionId,
            SanitizeUploadFileName(file.FileName),
            string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
            file.Length,
            DateTimeOffset.FromUnixTimeMilliseconds(lastModified),
            async path =>
            {
                var output = File.Create(path);
                await using (output.ConfigureAwait(false))
                {
                    var input = file.OpenReadStream();
                    await using (input.ConfigureAwait(false))
                    {
                        await input.CopyToAsync(output, ctx.RequestAborted).ConfigureAwait(false);
                    }
                }
            },
            options.MaxBytesPerSession).ConfigureAwait(false);
    }

    private static async Task WriteStagedAsync(HttpContext ctx, List<SessionUploadStore.Entry> staged)
    {
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
        // before consuming the one-shot entry. Peek, not Get, for the reason HandleUploadAsync gives.
        var session = sessions.Peek(sessionId);
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
                var fs = File.OpenRead(tempPath);
                await using (fs.ConfigureAwait(false))
                {
                    await fs.CopyToAsync(ctx.Response.Body, ctx.RequestAborted).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            SessionDownloadStore.Release(entry);
        }
    }

    private sealed partial class ServerRuntimeScript : IRaskRuntimeScript
    {
        public Component Render() => Script.Src(LiveOptions.PathBase + RuntimePath + "?v=" + Runtime.Value.Hash);
    }

    internal sealed class RaskLiveMarker
    {
        public bool RuntimeMapped;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rask {Version} (Server) starting")]
    private static partial void Starting(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "Rask: ShutdownDrainTimeout ({Drain}) is not below HostOptions.ShutdownTimeout ({Shutdown}), so live "
        + "sessions will be aborted at shutdown (the browser sees an abnormal 1006 close and reports a timed-out "
        + "session) instead of closed cleanly. Raise ShutdownTimeout or lower ShutdownDrainTimeout. Note that "
        + "HostOptions.ServicesStopConcurrently is false by default, so other hosted services spend from the same "
        + "budget before Rask's drain is even entered.")]
    private static partial void TightShutdownLadder(ILogger logger, TimeSpan drain, TimeSpan shutdown);
}
