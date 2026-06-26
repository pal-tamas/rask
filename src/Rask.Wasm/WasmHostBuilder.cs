using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Browser;
using Rask.Core.Diagnostics;
using Rask.Core.Forms;
using Rask.Core.Live;
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
        Services.AddSingleton<IBrowserStorage, BrowserStorage>();
        Services.AddSingleton<IClipboard, Clipboard>();
        Services.AddSingleton<IGeolocation, Geolocation>();
        Services.AddSingleton<INavigatorInfo, NavigatorInfo>();
        Services.AddSingleton<INetworkInfo, NetworkInfo>();
        Services.AddSingleton<IMediaQuery, MediaQuery>();
        Services.AddSingleton<ISpeechSynthesis, SpeechSynthesis>();
        Services.AddSingleton<ICookies, Cookies>();
        Services.AddSingleton<IPermissions, Permissions>();
        Services.AddSingleton<IShare, Share>();
        Services.AddSingleton<IWebPush, WebPush>();
        Services.AddSingleton<INotifications, Notifications>();
        Services.AddSingleton<IBadge, Badge>();
        Services.AddSingleton<IWakeLock, WakeLock>();
        Services.AddSingleton<IScreenOrientation, ScreenOrientation>();
        Services.AddSingleton<IFullscreen, Fullscreen>();
        Services.AddSingleton<IVibration, Vibration>();
        Services.AddSingleton<IPageVisibility, PageVisibilityInfo>();
        Services.TryAddSingleton<IUserProvider, AnonymousUserProvider>();
        Services.TryAddSingleton<IAuthSignIn, WasmAuthSignIn>();
        Services.AddAuthorizationCore();
        // IJSRuntime backed by the WASM JSImport/JSExport bridge. Singleton — one
        // runtime per app instance. JSInterop.Init(...) binds it to the bridge.
        Services.AddSingleton<WasmJSRuntime>();
        Services.AddSingleton<IJSRuntime>(sp => sp.GetRequiredService<WasmJSRuntime>());
    }

    private WebAppManifest? _manifest;

    /// <summary>The DI container for the app. Register your services here before calling <see cref="RunAsync{TApp}" />.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    ///     Makes the app an installable PWA from a typed <see cref="WebAppManifest" /> — the framework
    ///     injects the <c>&lt;link rel="manifest"&gt;</c> (a <c>data:</c> URL with sub-path-correct absolute
    ///     URLs) and <c>&lt;meta name="theme-color"&gt;</c> at boot, so there's no <c>manifest.webmanifest</c>
    ///     to hand-write. Call before <see cref="RunAsync{TApp}" />:
    ///     <code>
    ///     host.UseManifest(new WebAppManifest { Name = "My App", ThemeColor = "#512BD4",
    ///         Icons = [new ManifestIcon("icon.svg", "any", "image/svg+xml", "any maskable")] });
    ///     </code>
    /// </summary>
    public WasmHostBuilder UseManifest(WebAppManifest manifest)
    {
        _manifest = manifest;
        return this;
    }

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
        if (configureLive is not null)
        {
            var opts = new RaskLiveOptions();
            configureLive(opts);
            LiveOptions.DiffMode = opts.DiffMode;
            LiveOptions.PreloadScopedAssets = opts.PreloadScopedAssets;
            // Only propagate a non-empty user-supplied PathBase; an explicit
            // override should win over the auto-detect that RunAsync performs
            // later. An empty value here means "I didn't set one — auto-detect
            // from <base href> at boot."
            if (opts.PathBase.Length > 0)
            {
                LiveOptions.PathBase = opts.PathBase;
            }
        }
        // No configure → leave LiveOptions.DiffMode at whatever the framework default
        // is (LiveDiffMode.Auto). Don't write to it here: a test or host that pre-set
        // the field before calling CreateDefault() would otherwise be clobbered.

        return new WasmHostBuilder();
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

        var session = new WasmLiveSession(root, provider);
        JSInterop.Init(session);

        var payload = await session.InitialRenderAsync().ConfigureAwait(false);
        Console.WriteLine($"[Rask.Wasm] first render payload bytes={payload.Length}");
        if (payload.Length > 0)
        {
            JSInterop.ApplyRender(payload);
        }

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
