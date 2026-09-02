using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Rask.Meta.Hosting;

/// <summary>
///     Registers the supervised Node process and the forwarding machinery in front of it.
/// </summary>
public static class RaskMetaServiceCollectionExtensions
{
    /// <summary>
    ///     Adds hosting for a meta framework front end running as a supervised Node process.
    /// </summary>
    /// <remarks>
    ///     Registration is where the options live, rather than at
    ///     <see cref="RaskMetaEndpointExtensions.UseRaskMeta" />, because the supervisor needs them
    ///     before the pipeline is built — it has to start the process and wait for it to listen while
    ///     the app is still coming up.
    /// </remarks>
    /// <param name="services">The app's service collection.</param>
    /// <param name="configure">Adjusts <see cref="MetaHostingOptions" />.</param>
    /// <returns><paramref name="services" />, for chaining.</returns>
    public static IServiceCollection AddRaskMeta(
        this IServiceCollection services,
        Action<MetaHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new MetaHostingOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<NodeReadiness>();
        services.AddHttpForwarder();
        services.TryAddSingleton<NodeForwarder>();
        services.AddSingleton<IHostedService, NodeSupervisor>();

        return services;
    }
}
