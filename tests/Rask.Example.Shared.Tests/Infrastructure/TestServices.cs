using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core.Routing;
using Rask.Example.Shared.Demos;

namespace Rask.Example.Shared.Tests.Infrastructure;

// Builds a service provider with everything the example pages need to render — the
// same registrations the production hosts do, minus host-specific runtime details.
// Tests can override individual services by passing custom instances.
internal static class TestServices
{
    public static IServiceProvider Default(
        HttpClient? http = null,
        IJSRuntime? js = null,
        RouteState? routeState = null,
        IDownloadSink? downloadSink = null,
        IBannedWordService? bannedWords = null)
    {
        var sc = new ServiceCollection();

        routeState ??= new RouteState();
        sc.AddSingleton(routeState);
        sc.AddSingleton(sp => new Navigator(sp.GetRequiredService<RouteState>(),
            sp.GetService<IDownloadSink>()));

        sc.AddSingleton(http ?? new HttpClient { BaseAddress = new Uri("https://example.test/") });
        sc.AddSingleton<IJSRuntime>(js ?? new FakeJsRuntime());
        sc.AddSingleton<IDownloadSink>(downloadSink ?? new CapturingDownloadSink());
        sc.AddSingleton<IBannedWordService>(bannedWords ?? new BannedWordService());

        return sc.BuildServiceProvider();
    }
}
