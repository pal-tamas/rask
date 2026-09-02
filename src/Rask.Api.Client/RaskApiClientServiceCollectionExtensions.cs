using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Api.Client;

/// <summary>
///     Registers the typed API clients generated from this app's endpoints.
/// </summary>
public static class RaskApiClientServiceCollectionExtensions
{
    /// <summary>
    ///     Registers every generated API client, so a component can inject one by its own type.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    ///     No <see cref="HttpClient" /> is available and no <see cref="ApiClientOptions.BaseAddress" />
    ///     was set, so a call would have nowhere to go.
    /// </exception>
    public static IServiceCollection AddRaskApiClient(
        this IServiceCollection services,
        Action<ApiClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new ApiClientOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);

        // Each client is resolved from the registry rather than named here: the client types live in the
        // consumer's compilation, so this package cannot name them, and the generated module initializer
        // has already run by the time anything resolves one.
        foreach (var client in ApiClientRegistry.All)
        {
            var registration = client;

            services.TryAddScoped(
                registration.ClientType,
                provider => registration.Factory(
                    Resolve(provider, provider.GetRequiredService<ApiClientOptions>()),
                    provider.GetRequiredService<ApiClientOptions>()));
        }

        return services;
    }

    // An absolute BaseAddress means "build a client for it". Otherwise take the container's, which in a
    // browser app is the page origin — the right answer, and the one every Rask WASM host registers.
    private static HttpClient Resolve(IServiceProvider provider, ApiClientOptions options)
    {
        if (options.BaseAddress is { IsAbsoluteUri: true })
        {
            return new HttpClient { BaseAddress = options.BaseAddress, Timeout = options.Timeout };
        }

        var existing = provider.GetService<HttpClient>();

        if (existing?.BaseAddress is not null)
        {
            return existing;
        }

        throw new InvalidOperationException(
            "AddRaskApiClient() has nowhere to send a request. Set ApiClientOptions.BaseAddress to the "
            + "API's absolute address, or register an HttpClient whose BaseAddress is the app's own "
            + "origin — which is what a Rask WASM host already does.");
    }
}
