using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Example.Shared.Demos;

namespace Rask.Example.Shared;

// Centralized DI registration for the example apps — used by both Rask.Example.Server
// and Rask.Example.Wasm so neither has to maintain its own copy. Tests call this
// extension to assert the registration shape without booting an HTTP or WASM host.
public static class ExampleServiceCollectionExtensions
{
    // The HttpClient base address is host-specific: each host points it at its own
    // origin so the HttpClient + DI demo fetches data/posts-1.json from the static
    // files it serves itself (no external API). Server derives the origin from
    // IServerAddressesFeature; WASM uses WasmHostBuilder.BaseAddress (which carries
    // any sub-path). The resolver is invoked lazily on first HttpClient resolution,
    // by which point both sources are populated.
    public static IServiceCollection AddExampleServices(
        this IServiceCollection services,
        Func<IServiceProvider, Uri> httpBaseAddress)
    {
        services.AddSingleton(sp => new HttpClient { BaseAddress = httpBaseAddress(sp) });
        services.AddSingleton<IBannedWordService, BannedWordService>();

        // Toggleable demo auth for the User-gating showcase (/user). Registered as the concrete
        // type (so the demo can sign in/out) and as IUserProvider (so injected consumers resolve it).
        // Defaults to anonymous, so other pages are unaffected. Scoped — NOT singleton — so each
        // live session gets its own principal (matching the framework's own SessionUserProvider,
        // RaskEndpointExtensions.AddScoped). A singleton would share one signed-in user across every
        // Server connection, leaking auth state between sessions (and between E2E tests). On WASM
        // there's a single session, so scoped resolves once from the root provider — same behaviour.
        services.AddScoped<DemoUserProvider>();
        services.AddScoped<IUserProvider>(sp => sp.GetRequiredService<DemoUserProvider>());
        return services;
    }
}
