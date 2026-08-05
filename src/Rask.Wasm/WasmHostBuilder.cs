using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;
using Rask.Client.Browser;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Browser;
using Rask.Core.Diagnostics;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Core.Messaging;
using Rask.Core.Routing;
using Rask.Wasm.Authentication;
using Rask.Wasm.Browser;
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
        Services.AddSingleton<IBrowserFileBackend, WasmFileBackend>();
        Services.AddSingleton<IDownloadSink, WasmDownloadSink>();
        Services.AddSingleton<Navigator>();
        // Transient user messages / toasts (a flash-message pattern). Singleton = one queue for the app instance
        // (the whole WASM app is a single session), so a message queued before a NavigateTo survives it.
        Services.AddSingleton<IToaster, Toaster>();
        // Typed browser/device API wrappers, Singleton (one per app instance). Registered via the shared
        // helpers (RaskBrowserApis / RaskClientBrowserApis / RaskWasmBrowserApis) so the interface → impl list
        // lives in one place instead of duplicated across hosts. TryAdd inside the helpers means an app can
        // pre-register a better implementation and win. WASM serves all three tiers: the transport-agnostic
        // Core set, the in-process IShare, and the WASM-only device/handle set.
        Services.AddCoreBrowserApis(ServiceLifetime.Singleton);
        Services.AddClientBrowserApis(ServiceLifetime.Singleton);
        Services.AddWasmBrowserApis(ServiceLifetime.Singleton);
        Services.TryAddSingleton<IUserProvider, AnonymousUserProvider>();
        Services.TryAddSingleton<IAuthSignIn, WasmAuthSignIn>();
        Services.AddAuthorizationCore();
        // IJSRuntime backed by the WASM JSImport/JSExport bridge. Singleton — one
        // runtime per app instance. JSInterop.Init(...) binds it to the bridge.
        Services.AddSingleton<WasmJSRuntime>();
        Services.AddSingleton<IJSRuntime>(sp => sp.GetRequiredService<WasmJSRuntime>());
    }

    private WebAppManifest? _manifest;

    // The wire-payload shape this app renders with, snapshotted from RaskLiveOptions in CreateDefault
    // and handed to the WasmLiveSession — a per-session value instead of the former process-global
    // LiveOptions.DiffMode static. WASM is single-threaded so it never raced, but carrying it on the
    // session keeps all three hosts on one uniform mechanism.
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
    /// <typeparam name="TApp">The root <see cref="Component" /> for the app. Must render a complete shell (RASK021).</typeparam>
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

        // Bind the singleton JSRuntime into the static [JSImport]/[JSExport] bridge
        // BEFORE any user code can resolve IJSRuntime and start dispatching.
        JSInterop.Init(provider.GetRequiredService<WasmJSRuntime>());

        var app = ActivatorUtilities.CreateInstance<TApp>(provider);
        // Wrap the App in an implicit RootErrorBoundary so an uncaught render / lifecycle /
        // event-handler exception anywhere in the user's tree renders a styled fallback
        // page instead of leaving the browser on a blank screen.
        var root = new RootErrorBoundary(app);

        var routeState = provider.GetRequiredService<RouteState>();
        RouteSeeder.Seed(JSInterop.GetLocation(), routeState);
        Console.WriteLine($"[Rask.Wasm] initial path={routeState.Path}");

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
    }
}
