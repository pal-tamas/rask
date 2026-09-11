using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rask.Hosting.Shared;

namespace Rask.Api;

/// <summary>
///     Registers what a Rask app needs to host HTTP endpoints.
/// </summary>
public static class RaskApiServiceCollectionExtensions
{
    /// <summary>
    ///     Adds API hosting: the options, and MVC's controller services, which <c>MapRaskApi</c> maps
    ///     unless <see cref="ApiOptions.Controllers" /> is off.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options, after the <c>Rask:Api</c> configuration section.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    ///     <see cref="ApiOptions" /> reads the <c>Rask:Api</c> configuration section first and then
    ///     <paramref name="configure" />, so code wins. In an app that calls this twice the <b>first</b>
    ///     call's options win and the second one's configuration is discarded, the same as <c>AddRask</c>.
    ///     Configure it once.
    /// </remarks>
    public static IServiceCollection AddRaskApi(
        this IServiceCollection services,
        Action<ApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRaskOptions<ApiOptions>("Rask:Api", static (section, o) => section.Bind(o), configure,
            validate: null);

        // Registered whether or not controllers end up on: Rask:Api:Controllers is not readable until the
        // options are built, and MapRaskApi — which reads the built options — is what decides whether any
        // controller is mapped. With controllers off, what this leaves behind is inert service descriptors.
        //
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

        return services;
    }
}
