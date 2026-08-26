using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Browser;
using Rask.Core.Diagnostics;
using Rask.Core.Forms;
using Rask.Core.Globalization;
using Rask.Core.Live;
using Rask.Core.Messaging;
using Rask.Core.Routing;
using Rask.Wasm.Authentication;
using Rask.Wasm.Browser;
using Rask.Wasm.Diagnostics;
using Rask.Wasm.Files;

namespace Rask.Wasm;

/// <summary>
///     Entry point for a browser-WASM Rask app. Build with <see cref="CreateDefault()" />, register
///     app services on <see cref="Services" />, then <c>await</c> <see cref="RunAsync{TApp}" /> with the
///     root component. Mirrors <c>Rask.Server</c>'s <c>AddRask</c>/<c>UseRask</c> pair for the
///     JSImport/JSExport transport.
/// </summary>
public sealed class WasmHostBuilder
{
    private WasmHostBuilder()
    {
        Services = new ServiceCollection();
        Services.AddLogging();
        Services.AddSingleton<RouteState>();
        // The declared state bag. Singleton here for the same reason RouteState is: the whole WASM app is a
        // single session. It works exactly as it does on the server — what differs is that nothing carries
        // it anywhere, because a WASM app has no server-side session to rebuild. Registered so a component
        // shared between the two hosts resolves on both instead of failing DI only on this one.
        Services.AddSingleton<PersistentState>();
        Services.AddSingleton<IPersistentState>(sp => sp.GetRequiredService<PersistentState>());
        Services.AddSingleton<IBrowserFileBackend, WasmFileBackend>();
        Services.AddSingleton<IDownloadSink, WasmDownloadSink>();
        Services.AddSingleton<Navigator>();
        // Transient user messages / toasts (a flash-message pattern). Singleton = one queue for the app instance
        // (the whole WASM app is a single session), so a message queued before a NavigateTo survives it.
        Services.AddSingleton<IToaster, Toaster>();

        // Singleton, unlike the server's scoped registration: the whole WASM app is one visitor, so
        // there is exactly one culture for its lifetime.
        // Deferred to a factory so UseCulture can still be called after the builder is constructed —
        // the ctor runs before the app's own configuration does.
        Services.AddRaskCulture(o => _configureCulture?.Invoke(o), ServiceLifetime.Singleton);
        // Typed browser/device API wrappers, Singleton (one per app instance). Registered via the shared
        // helpers (RaskBrowserApis / RaskWasmBrowserApis) so the interface → impl list lives in one place
        // instead of duplicated across hosts. TryAdd inside the helpers means an app can pre-register a
        // better implementation and win. WASM serves both tiers: the transport-agnostic Core set, and the
        // WASM-only set that needs a live document, a device chooser or a user gesture.
        Services.AddCoreBrowserApis(ServiceLifetime.Singleton);
        Services.AddWasmBrowserApis(ServiceLifetime.Singleton);
        Services.TryAddSingleton<IUserProvider, AnonymousUserProvider>();
        // WasmAuthSignIn posts sign-out to the server, so it needs an HttpClient. Registering the sign-in
        // service without one made IAuthSignIn — a contract Rask.Core promises on every host — resolvable on
        // Server but a DI failure here, at the injection site, in any app that hadn't happened to
        // register an HttpClient of its own. TryAdd, so an app that registers one (typed clients, a handler
        // chain, a different base address) still wins.
        //
        // The factory is lazy on purpose: BaseAddress reads the page origin back through the JS module, which
        // only answers after RunAsync has imported it. Resolving before then (or off-browser, in a test)
        // yields a relative "/" — not a legal HttpClient.BaseAddress — so leave it unset in that case rather
        // than throwing out of a service factory.
        Services.TryAddSingleton(_ =>
            Uri.TryCreate(BaseAddress, UriKind.Absolute, out var origin)
                ? new HttpClient { BaseAddress = origin }
                : new HttpClient());
        Services.TryAddSingleton<IAuthSignIn, WasmAuthSignIn>();
        Services.AddAuthorizationCore();
        // IJSRuntime backed by the WASM JSImport/JSExport bridge. Singleton — one
        // runtime per app instance. JSInterop.Init(...) binds it to the bridge.
        Services.AddSingleton<WasmJSRuntime>();
        Services.AddSingleton<IJSRuntime>(sp => sp.GetRequiredService<WasmJSRuntime>());
    }

    private WebAppManifest? _manifest;
    private Action<RaskCultureOptions>? _configureCulture;

    // The wire-payload shape this app renders with, snapshotted from RaskLiveOptions in CreateDefault
    // and handed to the WasmLiveSession — a per-session value instead of the former process-global
    // LiveOptions.DiffMode static. WASM is single-threaded so it never raced, but carrying it on the
    // session keeps both hosts on one uniform mechanism.
    private LiveDiffMode _diffMode = LiveDiffMode.Auto;

    /// <summary>The DI container for the app. Register your services here before calling <see cref="RunAsync{TApp}" />.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    ///     Makes the app an installable PWA from a typed <see cref="WebAppManifest" /> — the framework
    ///     injects the <c>&lt;link rel="manifest"&gt;</c> (a <c>data:</c> URL with sub-path-correct absolute
    ///     URLs) and <c>&lt;meta name="theme-color"&gt;</c> at boot, so there's no <c>manifest.webmanifest</c>
    ///     to hand-write. The WASM counterpart to the Server host's <c>AddRaskPwa</c>. Call before
    ///     <see cref="RunAsync{TApp}" />:
    ///     <code>
    ///     host.UsePwa(new WebAppManifest { Name = "My App", ThemeColor = "#512BD4",
    ///         Icons = [new ManifestIcon("icon.svg", "any", "image/svg+xml", "any maskable")] });
    ///     </code>
    /// </summary>
    public WasmHostBuilder UsePwa(WebAppManifest manifest)
    {
        _manifest = manifest;
        return this;
    }

    /// <summary>
    ///     The languages this app ships, and how a visitor's is chosen.
    /// </summary>
    /// <remarks>
    ///     Leaving this uncalled keeps culture support off, and the app renders exactly as it did before:
    ///     <c>&lt;html lang="en"&gt;</c> with no <c>dir</c>.
    ///     <para>
    ///         <b>A WASM app also needs ICU</b>, which Rask does not ship by default because it is roughly
    ///         2.6 MB. Add <c>&lt;RaskGlobalization&gt;true&lt;/RaskGlobalization&gt;</c> to the project
    ///         file. Without it every culture formats identically and only the invariant culture
    ///         resolves — the app still runs, and says so once at startup rather than once per render.
    ///     </para>
    ///     <code>
    ///     host.UseCulture(c => { c.SupportedCultures.Add("en"); c.SupportedCultures.Add("hu"); });
    ///     </code>
    /// </remarks>
    public WasmHostBuilder UseCulture(Action<RaskCultureOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configureCulture = configure;
        return this;
    }

    /// <summary>Renamed to <see cref="UsePwa" /> for naming parity with the Server host's <c>AddRaskPwa</c>.</summary>
    [Obsolete("Renamed to UsePwa (parity with the Server host's AddRaskPwa). This alias will be removed.")]
    public WasmHostBuilder UseManifest(WebAppManifest manifest) => UsePwa(manifest);

    /// <summary>
    ///     Page origin (e.g. "https://localhost:5050/") suitable for use as <see cref="HttpClient.BaseAddress" />.
    ///     Read this lazily inside an <see cref="IServiceCollection" /> factory so the call happens after
    ///     <see cref="RunAsync{TApp}" /> has imported the JS module.
    /// </summary>
    public static string BaseAddress => JSInterop.GetBaseAddress();

    /// <summary>Creates a builder with framework-default live options (<see cref="LiveDiffMode.Auto" />).</summary>
    public static WasmHostBuilder CreateDefault() => CreateDefault(null);

    /// <summary>
    ///     WASM-side bootstrap. Defaults to <see cref="LiveDiffMode.Auto" /> — the
    ///     diff codec is end-to-end validated on both the <c>Rask.Server</c> live
    ///     runtime (164/164 Playwright tests) and the WASM runtime (266/266
    ///     Playwright tests + 107 standalone WASM tests). Override with:
    ///     <code>
    ///         WasmHostBuilder.CreateDefault(o => o.DiffMode = LiveDiffMode.DisabledFull)
    ///     </code>
    ///     to restore the pre-codec ship-full-HTML behaviour.
    /// </summary>
    public static WasmHostBuilder CreateDefault(Action<RaskLiveOptions>? configureLive)
    {
        var builder = new WasmHostBuilder();
        if (configureLive is not null)
        {
            var opts = new RaskLiveOptions();
            configureLive(opts);
            builder._diffMode = opts.DiffMode;
            // Only propagate a non-empty user-supplied PathBase; an explicit
            // override should win over the auto-detect that RunAsync performs
            // later. An empty value here means "I didn't set one — auto-detect
            // from <base href> at boot."
            if (opts.PathBase.Length > 0)
            {
                LiveOptions.PathBase = opts.PathBase;
            }
        }
        // No configure → the session renders in the framework default (LiveDiffMode.Auto).

        return builder;
    }

    /// <summary>
    ///     Boots the app: imports the JS bridge, auto-detects the path base from <c>&lt;base href&gt;</c>,
    ///     builds the service provider, instantiates <typeparamref name="TApp" /> (wrapped in a root error
    ///     boundary), and performs the first render. Returns once the initial render has been applied.
    /// </summary>
    /// <typeparam name="TApp">
    ///     The root <see cref="Component" /> for the app. It renders into <c>&lt;body&gt;</c>; Rask composes
    ///     the document around it (RASK021 flags a root that builds the shell itself).
    /// </typeparam>
    public async Task RunAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>()
        where TApp : Component
    {
        Console.WriteLine($"[Rask.Wasm] Rask {RaskVersion.Current} (WASM) starting");
        Console.WriteLine("[Rask.Wasm] importing rask.wasm.js …");
        await JSInterop.ImportJsModuleAsync().ConfigureAwait(false);
        Console.WriteLine("[Rask.Wasm] rask.wasm.js imported");

        // Auto-detect the app's sub-path from <base href> so head-emitted asset
        // URLs (e.g. /Rask/_rask/a/{hash}.css for a GH Pages deploy at /Rask/)
        // resolve correctly. Skipped if the user already configured PathBase
        // explicitly in CreateDefault(o => o.PathBase = ...) — the static
        // accessor is non-empty in that case.
        if (LiveOptions.PathBase.Length == 0)
        {
            LiveOptions.PathBase = RaskPath.Normalize(JSInterop.GetBasePath());
            Console.WriteLine($"[Rask.Wasm] auto-detected PathBase='{LiveOptions.PathBase}'");
        }

        var provider = Services.BuildServiceProvider();

        // Route framework diagnostics into the app's own logging before anything can report one. Until
        // this existed, WASM was the one host where a swallowed framework fault never reached the app's
        // configured providers at all — it went to the seam's stderr default and nowhere else.
        RaskWasmDiagnostics.Install(provider.GetService<ILoggerFactory>());

        // Bind the singleton JSRuntime into the static [JSImport]/[JSExport] bridge
        // BEFORE any user code can resolve IJSRuntime and start dispatching.
        JSInterop.Init(provider.GetRequiredService<WasmJSRuntime>());

        var app = ActivatorUtilities.CreateInstance<TApp>(provider);
        // Wrap the App in an implicit RootErrorBoundary so an uncaught render / lifecycle /
        // event-handler exception anywhere in the user's tree renders a styled fallback
        // page instead of leaving the browser on a blank screen.
        // A chain carries properties and DI services; `app` is neither, and this is the root, so there is
        // no parent render context whose GetOrCreate a chain would route through. RASK014's reason to
        // exist is absent here.
#pragma warning disable RASK014
        var root = new RootErrorBoundary(app);
#pragma warning restore RASK014

        var routeState = provider.GetRequiredService<RouteState>();
        RouteSeeder.Seed(JSInterop.GetLocation(), routeState);
        Console.WriteLine($"[Rask.Wasm] initial path={routeState.Path}");

        // The visitor's language, settled before the first render for the same reason the route is:
        // painting in the wrong one and correcting it afterwards is a flash the visitor sees.
        WasmCultureSeeder.Seed(provider);

        if (provider.GetService<IUserProvider>() is { } userProvider)
        {
            try { await userProvider.EnsureLoadedAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                RaskDiagnostics.Report(
                    RaskLogLevel.Error,
                    "Rask.Wasm",
                    "[Rask.Wasm] IUserProvider.EnsureLoadedAsync failed",
                    ex);
            }
        }

        var session = new WasmLiveSession(root, provider, _diffMode);
        JSInterop.Init(session);

        // The session registers itself with the hot-reload coordinator in the base ctor and is
        // repainted from there; this only adds the "applied" indicator. No-op unless the runtime
        // supports metadata updates, which a trimmed (published) bundle does not.
        HotReload.WasmHotReloadBridge.Subscribe();

        // InitialRenderAsync builds and pushes the first frame to JS itself (zero-copy applyRender);
        // the returned bytes are just for the diagnostic below.
        var payload = await session.InitialRenderAsync().ConfigureAwait(false);
        Console.WriteLine($"[Rask.Wasm] first render payload bytes={payload.Length}");
        Console.WriteLine("[Rask.Wasm] first render applied");

        // Inject the typed web app manifest (if configured) — a data: URL <link rel="manifest"> with
        // sub-path-correct absolute URLs, plus <meta name="theme-color">. Non-fatal on failure.
        if (_manifest is not null)
        {
            try
            {
                await provider.GetRequiredService<IJSRuntime>()
                    .InvokeVoidAsync("__raskPwa.applyManifest", _manifest.ToJson())
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RaskDiagnostics.Report(
                    RaskLogLevel.Warning, "Rask.Wasm", "[Rask.Wasm] applying web app manifest failed", ex);
            }
        }

        // Registered IHostedServices — the browser analogue of the generic host starting them, so an
        // AddHostedService line means the same thing on both hosts instead of silently doing nothing here.
        //
        // LAST in the boot sequence, deliberately, for two reasons. A background service is free to mutate
        // state and call StateHasChanged, and until InitialRenderAsync has run there is no mounted tree to
        // render into. And a plain IHostedService (not a BackgroundService) does its work *inside*
        // StartAsync, so starting these any earlier would let a slow one delay the manifest injection — or,
        // if it never returns, hold up everything after `await RunAsync<App>()` in the user's Program.cs
        // with no clue as to why. Nothing after this point can be starved.
        var hostedServices = new WasmHostedServices(provider);
        JSInterop.Init(hostedServices);
        await hostedServices.StartAsync().ConfigureAwait(false);
    }
}
