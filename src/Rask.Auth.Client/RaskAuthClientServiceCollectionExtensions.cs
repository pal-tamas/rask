using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Core.Authentication;

namespace Rask.Auth.Client;

/// <summary>How the browser half reaches the app's auth endpoints.</summary>
public sealed class AuthClientOptions
{
    private string _prefix = AuthApi.DefaultPrefix;

    /// <summary>
    /// The path the endpoints sit under. Must match the server's <c>AuthOptions.ApiPrefix</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The value is empty, or does not start with <c>/</c>.</exception>
    public string Prefix
    {
        get => _prefix;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            if (!value.StartsWith('/'))
            {
                throw new ArgumentException(
                    $"The auth API prefix must start with '/', but was '{value}'.", nameof(value));
            }

            _prefix = value.Length > 1 ? value.TrimEnd('/') : value;
        }
    }
}

/// <summary>Registers the browser half of Rask.Auth.</summary>
public static class RaskAuthClientServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IUserProvider" /> and <see cref="IAuth" /> for a browser host, over the app's own
    /// <c>/api/auth</c> endpoints.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options.</param>
    /// <remarks>
    /// <para>
    /// The <see cref="HttpClient" /> comes from the container, which on a Rask WebAssembly host already
    /// has the page origin as its base address — so the calls are same-origin and the auth cookie rides
    /// along without anything being attached by hand.
    /// </para>
    /// <para>
    /// <see cref="IUserProvider" /> replaces the anonymous default the browser host registers, so this
    /// call is the whole of what a WebAssembly app needs to know who is signed in.
    /// </para>
    /// <example>
    /// <code>
    /// var builder = WasmHostBuilder.CreateDefault();
    /// builder.Services.AddRaskAuthClient();
    /// await builder.RunAsync&lt;App&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    public static IServiceCollection AddRaskAuthClient(
        this IServiceCollection services, Action<AuthClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new AuthClientOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);

        // Not TryAdd: the browser host registers AnonymousUserProvider as the default, and the whole
        // point of this call is to replace it. TryAdd would leave every page anonymous forever, which
        // is the kind of failure that looks like "auth does not work" and points nowhere.
        services.AddSingleton<IUserProvider, HttpUserProvider>();
        services.TryAddSingleton<IAuth, BrowserAuth>();

        return services;
    }
}
