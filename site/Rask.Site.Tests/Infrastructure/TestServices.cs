using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core.Browser;
using Rask.Core.Messaging;
using Rask.Core.Routing;
using Rask.Site;
using Rask.Site.Features;
using Rask.Wasm.Browser;

namespace Rask.Site.Tests.Infrastructure;

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

        // The app's own registration, exactly as both real hosts call it, rather than a hand-maintained
        // copy that drifts — a demo ctor-injecting DemoUserProvider/IDispatcher/ITodoStore can't be
        // constructed without it. It goes FIRST so every override below re-registers over it and wins
        // (last registration wins for a single resolve). The base address is replaced below too, so the
        // resolver here is never invoked.
        sc.AddExampleServices(_ => new Uri("https://example.test/"));

        // The typed browser wrappers and the toaster, exactly as the real hosts register them — demos
        // ctor-inject IIntersectionObserver, IBrowserStorage, IToaster, …. The browser wrappers are
        // TryAdd fallbacks, so they never displace anything registered here.
        sc.AddCoreBrowserApis(ServiceLifetime.Singleton);
        // And the WASM-ONLY half, which was missing. Half the showcase — every device and PWA demo,
        // fourteen pages — ctor-injects one of these, so without it no unit test could render any of
        // them: the render faulted on "Unable to resolve service for type 'IEyeDropper'", the root
        // boundary caught it, and what came back was a perfectly ordinary error document.
        //
        // Registering them is safe off a browser. Each wrapper reaches JS lazily, inside the call, so
        // constructing one costs nothing; a page that only RENDERS never touches the interop, which is
        // exactly what these tests do and exactly what the prerender pass does.
        sc.AddWasmBrowserApis(ServiceLifetime.Singleton);
        sc.AddSingleton<IToaster, Toaster>();

        routeState ??= new RouteState();
        sc.AddSingleton(routeState);
        sc.AddSingleton(sp => new Navigator(sp.GetRequiredService<RouteState>(),
            sp.GetService<IDownloadSink>()));

        sc.AddSingleton(http ?? new HttpClient { BaseAddress = new Uri("https://example.test/") });
        sc.AddSingleton<IJSRuntime>(js ?? new FakeJsRuntime());
        sc.AddSingleton(downloadSink ?? new CapturingDownloadSink());
        sc.AddSingleton(bannedWords ?? new BannedWordService());
        return sc.BuildServiceProvider();
    }
}
