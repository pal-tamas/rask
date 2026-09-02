using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Api;

/// <summary>
///     Registers what a Rask app needs to host HTTP endpoints.
/// </summary>
public static class RaskApiServiceCollectionExtensions
{
    /// <summary>
    ///     Adds API hosting: the options, and MVC's controller services unless
    ///     <see cref="ApiOptions.Controllers" /> is off.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    ///     The options go in with <c>TryAddSingleton</c>, so in an app that calls this twice the
    ///     <b>first</b> call wins and the second one's configuration is discarded — the same shape, and
    ///     the same hazard, as <c>AddRask</c> (RASK056). Configure it once.
    /// </remarks>
    public static IServiceCollection AddRaskApi(
        this IServiceCollection services,
        Action<ApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new ApiOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);

        if (options.Controllers)
        {
            services.AddControllers();
        }

        return services;
    }
}
