using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Hosting.Shared;

namespace Rask.Cqrs.Server;

/// <summary>Registers the server half of Rask.Cqrs remote dispatch.</summary>
public static class RaskCqrsServerServiceCollectionExtensions
{
    /// <summary>
    ///     Registers Rask.Cqrs and the services <c>MapRaskCqrs()</c> needs. Call it once at startup — it
    ///     is the only Rask.Cqrs line a server project needs, and <c>AddRaskCqrs()</c> is called for you.
    /// </summary>
    /// <param name="services">The app's service collection.</param>
    /// <param name="configure">
    ///     Optional endpoint configuration — limits, route prefix, error detail — applied after the
    ///     <c>Rask:Cqrs:Server</c> configuration section.
    /// </param>
    /// <param name="configureCqrs">
    ///     Optional Rask.Cqrs configuration — handler lifetime, pipeline behaviors — applied after the
    ///     <c>Rask:Cqrs</c> configuration section.
    /// </param>
    /// <remarks>
    ///     Code wins over configuration for both. <c>Rask:Cqrs:Server:RequireAuthenticatedUser</c> included: remote
    ///     dispatch can be opened from the environment, which is what configuration is for and a reason to guard the
    ///     deploy environment's variables as closely as its code.
    /// </remarks>
    public static IServiceCollection AddRaskCqrsServer(
        this IServiceCollection services,
        Action<CqrsServerOptions>? configure = null,
        Action<CqrsOptions>? configureCqrs = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRaskOptions<CqrsServerOptions>(
            "Rask:Cqrs:Server", static (section, o) => section.Bind(o), configure, static o => o.Validate());

        // The one line: a server project references Rask.Cqrs.Server and calls this, nothing else.
        services.AddRaskCqrs(configureCqrs);

        // What decides a handler's [Authorize(Policy = …)], for the endpoint and for a local dispatch.
        services.TryAddTransient<IPolicyEvaluator, AuthorizationPolicyEvaluator>();

        // The half of a chunked upload that outlives a single request: parts land here until the message
        // that carries them arrives. Singleton because a session spans requests by definition, and
        // disposable because its parts are files on disk.
        services.TryAddSingleton(sp => new UploadSessionStore(
            sp.GetRequiredService<CqrsServerOptions>(),
            sp.GetService<TimeProvider>()));

        return services;
    }
}
