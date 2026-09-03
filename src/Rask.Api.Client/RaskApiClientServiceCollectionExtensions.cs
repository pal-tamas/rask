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

        // ONE HttpClient for the app, not one per scope. The clients are scoped (they are cheap, and a
        // scope is the natural lifetime for anything carrying per-request state), but the transport
        // underneath must not be: a new HttpClient is a new SocketsHttpHandler and a new connection
        // pool, nothing disposes it, and a server-rendered app would open one per request until it ran
        // out of sockets. Singleton also means DNS is not pinned per scope.
        services.TryAddSingleton(provider =>
        {
            var (client, owned) = Resolve(provider, options);
            return new ApiHttpClient(client, owned);
        });

        // Each client is resolved from the registry rather than named here: the client types live in the
        // consumer's compilation, so this package cannot name them, and the generated module initializer
        // has already run by the time anything resolves one.
        foreach (var client in ApiClientRegistry.All)
        {
            var registration = client;

            services.TryAddScoped(
                registration.ClientType,
                provider => registration.Factory(
                    provider.GetRequiredService<ApiHttpClient>().Value,
                    provider.GetRequiredService<ApiClientOptions>()));
        }

        return services;
    }

    /// <summary>
    ///     The one <see cref="HttpClient" /> every generated client sends on.
    /// </summary>
    /// <remarks>
    ///     A wrapper rather than registering <see cref="HttpClient" /> itself, because an app very often
    ///     already has one in the container — a Rask WASM host registers it at the page origin — and
    ///     taking that registration over would change what every other injection in the app resolves.
    ///     Owned by the container: disposed with it, and never per scope.
    /// </remarks>
    private sealed class ApiHttpClient(HttpClient value, bool owned) : IDisposable
    {
        public HttpClient Value { get; } = value;

        // Only what this package CREATED. When the transport is the container's own HttpClient — the
        // usual case in a browser app — disposing it here would tear down a client the app registered
        // and still uses elsewhere.
        public void Dispose()
        {
            if (owned)
            {
                Value.Dispose();
            }
        }
    }

    // An absolute BaseAddress means "build a client for it". Otherwise take the container's, which in a
    // browser app is the page origin — the right answer, and the one every Rask WASM host registers.
    private static (HttpClient Client, bool Owned) Resolve(IServiceProvider provider, ApiClientOptions options)
    {
        if (options.BaseAddress is { IsAbsoluteUri: true })
        {
            return (new HttpClient { BaseAddress = options.BaseAddress, Timeout = options.Timeout }, true);
        }

        var existing = provider.GetService<HttpClient>();

        if (existing?.BaseAddress is not null)
        {
            return (existing, false);
        }

        throw new InvalidOperationException(
            "AddRaskApiClient() has nowhere to send a request. Set ApiClientOptions.BaseAddress to the "
            + "API's absolute address, or register an HttpClient whose BaseAddress is the app's own "
            + "origin — which is what a Rask WASM host already does.");
    }
}
