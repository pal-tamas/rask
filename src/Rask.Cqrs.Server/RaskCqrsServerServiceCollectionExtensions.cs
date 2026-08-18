using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Cqrs.Server;

/// <summary>Registers the server half of Rask.Cqrs remote dispatch.</summary>
public static class RaskCqrsServerServiceCollectionExtensions
{
    /// <summary>
    ///     Registers Rask.Cqrs and the services <c>MapRaskCqrs()</c> needs. Call it once at startup — it
    ///     is the only Rask.Cqrs line a server project needs, and <c>AddRaskCqrs()</c> is called for you.
    /// </summary>
    /// <param name="services">The app's service collection.</param>
    /// <param name="configure">Optional endpoint configuration — limits, route prefix, error detail.</param>
    /// <param name="configureCqrs">Optional Rask.Cqrs configuration — handler lifetime, pipeline behaviors.</param>
    public static IServiceCollection AddRaskCqrsServer(
        this IServiceCollection services,
        Action<RaskCqrsServerOptions>? configure = null,
        Action<CqrsOptions>? configureCqrs = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RaskCqrsServerOptions();
        configure?.Invoke(options);
        options.Validate();

        // The one line: a server project references Rask.Cqrs.Server and calls this, nothing else.
        services.AddRaskCqrs(configureCqrs);

        services.TryAddSingleton(options);

        return services;
    }
}
