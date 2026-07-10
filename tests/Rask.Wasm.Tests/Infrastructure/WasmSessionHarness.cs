using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Wasm.Tests.Infrastructure;

/// <summary>
///     Builds a <see cref="WasmLiveSession" /> wired the way the session tests need it: a DI
///     container with <see cref="RouteState" /> + <see cref="Navigator" />, the app under test,
///     and <c>JSInterop.Init</c> so the static interop entry points target this session.
///     Consolidates the per-file <c>NewSession</c> copies. Imported as a static using so call
///     sites read <c>NewSession()</c> / <c>NewSession&lt;App&gt;()</c>.
/// </summary>
internal static class WasmSessionHarness
{
    public static (WasmLiveSession session, IServiceProvider services) NewSession(
        LiveDiffMode diffMode = LiveDiffMode.Auto) =>
        NewSession<StubApp>(diffMode: diffMode);

    public static (WasmLiveSession session, IServiceProvider services) NewSession<TApp>(
        Func<IServiceProvider, TApp>? appFactory = null,
        Action<IServiceCollection>? configure = null,
        LiveDiffMode diffMode = LiveDiffMode.Auto)
        where TApp : Component
    {
        var services = new ServiceCollection();
        services.AddSingleton<RouteState>();
        services.AddSingleton<Navigator>();
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();
        var app = appFactory is null ? ActivatorUtilities.CreateInstance<TApp>(provider) : appFactory(provider);
        // Per-session wire shape (was the process-global LiveOptions.DiffMode).
        var session = new WasmLiveSession(app, provider, diffMode);
        JSInterop.Init(session);
        return (session, provider);
    }
}
