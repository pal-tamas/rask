using Microsoft.Extensions.DependencyInjection;
using Rask.Example.Shared.Demos;

namespace Rask.Example.Shared;

// Centralized DI registration for the example apps — used by both Rask.Example.Server
// and Rask.Example.Wasm so neither has to maintain its own copy. Tests call this
// extension to assert the registration shape without booting an HTTP or WASM host.
public static class ExampleServiceCollectionExtensions
{
    public static IServiceCollection AddExampleServices(this IServiceCollection services)
    {
        services.AddSingleton(_ =>
            new HttpClient { BaseAddress = new Uri("https://jsonplaceholder.typicode.com/") });
        services.AddSingleton<IBannedWordService, BannedWordService>();
        return services;
    }
}
