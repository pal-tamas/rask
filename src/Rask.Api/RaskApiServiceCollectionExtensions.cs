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
            // AddMvcCore, not AddControllers. What an API controller needs is the core: routing,
            // model binding, the JSON formatters and the [ApiController] conventions. AddControllers
            // layers on the API explorer, CORS services and formatter mappings — machinery for
            // OpenAPI documents, cross-origin policies and `.json`-style URL suffixes that an app
            // gets whether or not it ever asks for any of them.
            //
            // DataAnnotations is the one addition, because leaving it out changes behaviour rather
            // than only weight: a [Required] or [Range] on a request body would silently stop being
            // enforced, and an endpoint that quietly accepts what it used to reject is worse than a
            // heavier registration. An app wanting CORS or an OpenAPI document adds AddCors() or
            // AddApiExplorer() itself, which is a line it would have written anyway.
            services.AddMvcCore().AddDataAnnotations();
        }

        return services;
    }
}
