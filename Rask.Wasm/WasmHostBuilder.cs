using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        Services.TryAddSingleton<RaskAuthorizationOptions>();
        Services.TryAddSingleton<IAuthSignIn, WasmAuthSignIn>();
        Services.AddAuthorizationCore();
    }

    public IServiceCollection Services { get; }

    /// <summary>
    ///     Page origin (e.g. "https://localhost:5050/") suitable for use as <see cref="HttpClient.BaseAddress" />.
    ///     Read this lazily inside an <see cref="IServiceCollection" /> factory so the call happens after
    ///     <see cref="RunAsync{TApp}" /> has imported the JS module.
    /// </summary>
    public static string BaseAddress => JSInterop.GetBaseAddress();

    public static WasmHostBuilder CreateDefault() => new();

    public async Task RunAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>()
        where TApp : Component
    {
        Console.WriteLine("[Rask.Wasm] importing rask.wasm.js …");
        await JSInterop.ImportJsModuleAsync().ConfigureAwait(false);
        Console.WriteLine("[Rask.Wasm] rask.wasm.js imported");

        var provider = Services.BuildServiceProvider();

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
