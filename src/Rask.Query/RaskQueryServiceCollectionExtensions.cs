using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Core.Live;

namespace Rask.Querying;

/// <summary>Registers the query cache.</summary>
public static class RaskQueryServiceCollectionExtensions
{
    /// <summary>
    ///     Adds <see cref="IQueryClient" />, scoped.
    /// </summary>
    /// <remarks>
    ///     <b>Scoped is the security boundary, not a performance choice.</b> Rask creates a service
    ///     scope per live session, so on the Server host — where one process serves every visitor —
    ///     this yields one cache per visitor and one visitor's orders can never be handed to another.
    ///     A singleton would share them, which is why this package offers no way to register one.
    ///     On WASM and native the scope is the whole app, which is the same thing.
    ///     <para>
    ///         Call <c>AddRaskCqrs()</c> as well; this wraps the dispatcher it registers.
    ///     </para>
    /// </remarks>
    /// <param name="services">The app's service collection.</param>
    /// <returns><paramref name="services" />, for chaining.</returns>
    public static IServiceCollection AddRaskQuery(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.Any(static d => d.ServiceType == typeof(IQueryClient)))
        {
            // The session's cache, not an event's: an event's scope is handed this same client (SessionScoped).
            services.TryAddScoped<SessionQueryClientOwner>();
            services.AddScoped<IQueryClient>(static sp => sp.GetRequiredService<SessionQueryClientOwner>().Client);
            SessionScoped.Mark(services, typeof(IQueryClient));
        }

        return services;
    }
}
