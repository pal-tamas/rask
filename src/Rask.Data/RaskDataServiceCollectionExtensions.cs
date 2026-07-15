using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Data;

/// <summary>Registers Rask.Data's EF Core interceptors into an <see cref="IServiceCollection"/>.</summary>
public static class RaskDataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the auditing, soft-delete, and (unless disabled) domain-event interceptors as
    /// <see cref="ISaveChangesInterceptor"/> services, plus a <see cref="TimeProvider"/>. Add them to a
    /// context with
    /// <c>o.AddInterceptors(sp.GetServices&lt;ISaveChangesInterceptor&gt;())</c> in your
    /// <c>AddDbContext(Factory)</c> callback, and call <c>modelBuilder.ApplyRaskConventions()</c> in
    /// <c>OnModelCreating</c>. Idempotent. Domain-event dispatch needs <c>AddRaskCqrs()</c>.
    /// </summary>
    public static IServiceCollection AddRaskData(this IServiceCollection services, Action<RaskDataOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RaskDataOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(TimeProvider.System);

        // Registration order is the interception order: soft-delete rewrites Deleted -> Modified first, so
        // auditing then stamps + versions the resulting update.
        if (!services.Any(static d => d.ImplementationType == typeof(SoftDeleteInterceptor)))
        {
            services.AddSingleton<ISaveChangesInterceptor, SoftDeleteInterceptor>();
            services.AddSingleton<ISaveChangesInterceptor, AuditingInterceptor>();

            if (options.DispatchDomainEventsInProcess)
            {
                services.AddSingleton<ISaveChangesInterceptor, DomainEventInterceptor>();
            }
        }

        return services;
    }
}
