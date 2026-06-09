using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Authorization;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Wasm.Authentication;
using Rask.Wasm.Files;

namespace Rask.Wasm;

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
        Services.TryAddSingleton<IUserProvider, AnonymousUserProvider>();
        Services.TryAddSingleton<IAuthSignIn, WasmAuthSignIn>();
        Services.AddAuthorizationCore();
        // IJSRuntime backed by the WASM JSImport/JSExport bridge. Singleton — one
        // runtime per app instance. JSInterop.Init(...) binds it to the bridge.
        Services.AddSingleton<WasmJSRuntime>();
        Services.AddSingleton<IJSRuntime>(sp => sp.GetRequiredService<WasmJSRuntime>());
    }

    public IServiceCollection Services { get; }

    /// <summary>
    ///     Page origin (e.g. "https://localhost:5050/") suitable for use as <see cref="HttpClient.BaseAddress" />.
    ///     Read this lazily inside an <see cref="IServiceCollection" /> factory so the call happens after
    ///     <see cref="RunAsync{TApp}" /> has imported the JS module.
    /// </summary>
    public static string BaseAddress => JSInterop.GetBaseAddress();

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

    public async Task RunAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>()
        where TApp : Component
    {
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
                Console.Error.WriteLine($"[Rask.Wasm] IUserProvider.EnsureLoadedAsync failed: {ex.Message}");
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
    }
}
