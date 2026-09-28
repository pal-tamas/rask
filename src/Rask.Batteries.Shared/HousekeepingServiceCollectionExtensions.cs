using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.Batteries;

/// <summary>Registers a battery's background service against a <see cref="HousekeepingContextFactory{TContext}" />.</summary>
internal static class HousekeepingServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <typeparamref name="TService" /> as a hosted service whose
    ///     <see cref="IDbContextFactory{TContext}" /> is the quiet one.
    /// </summary>
    /// <remarks>
    ///     <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)" />
    ///     is what makes a repeated <c>AddRaskX</c> register one service rather than two, and it
    ///     deduplicates on the descriptor's implementation type. A factory descriptor has none to read
    ///     directly, so it reports the factory delegate's own return type instead — which is why this
    ///     builds the descriptor through the two-argument generic overload, typing the delegate
    ///     <c>Func&lt;IServiceProvider, TService&gt;</c> rather than <c>Func&lt;IServiceProvider, object&gt;</c>.
    ///     Registering twice is pinned by each battery's own idempotency test.
    /// </remarks>
    /// <typeparam name="TService">The battery's background service.</typeparam>
    /// <typeparam name="TContext">The application <see cref="DbContext" /> that owns its tables.</typeparam>
    public static IServiceCollection AddHousekeepingService<TService, TContext>(this IServiceCollection services)
        where TService : class, IHostedService
        where TContext : DbContext
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TService>(
            static sp => ActivatorUtilities.CreateInstance<TService>(sp, new HousekeepingContextFactory<TContext>(sp))));

        return services;
    }
}
