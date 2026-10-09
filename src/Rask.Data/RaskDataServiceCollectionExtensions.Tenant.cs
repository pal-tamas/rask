using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <content>Where the tenant comes from, when it is not the signed-in user's claim.</content>
public static partial class RaskDataServiceCollectionExtensions
{
    /// <summary>
    ///     Says where the tenant of a request or a live session comes from — a host name, a header, a route
    ///     value, the app's own request service — in place of the signed-in user's <c>rask:tenant</c> claim.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="resolve">
    ///     Returns the tenant for the scope it is handed — a request's services, or a live session's — or
    ///     <see langword="null" /> when the work belongs to none.
    /// </param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    ///     <para>
    ///         <code>
    /// builder.Services.AddRaskTenant(sp =&gt; sp.GetRequiredService&lt;ICurrentRequest&gt;().TenantId);
    ///         </code>
    ///     </para>
    ///     <para>
    ///         It is asked once per scope and the answer is kept: as an HTTP request starts, and as a live
    ///         session opens — for as long as that session lives. <c>Current.Tenant</c>, every tenant filter
    ///         and every insert stamp then use it. An explicit <c>Tenant.Use</c> or <c>Tenant.Across</c> still
    ///         wins, so a background job goes on running in the tenant its row recorded.
    ///     </para>
    ///     <para>
    ///         <b>Once registered, it decides.</b> The claim is no longer the source: a signed-in user with no
    ///         tenant claim works in the resolved tenant, and one whose claim names a DIFFERENT tenant is
    ///         refused with a <see cref="Rask.Cqrs.ForbiddenException" /> the moment the tenant is read.
    ///     </para>
    ///     <para>
    ///         <b>When it names no tenant</b> a tenant-scoped table reads as EMPTY and a write to one is
    ///         refused with a <see cref="MissingTenantException" /> — never an exception on a read, and never
    ///         every tenant's rows. Without a resolver nothing changes: a read with no tenant still throws.
    ///     </para>
    ///     <para>
    ///         The scope it is handed is the one <see cref="Db.UseScope" /> made ambient. A live session's
    ///         scope is not a request's, so read the request through <c>IHttpContextAccessor</c> rather than
    ///         through a scoped object a middleware filled in.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">A resolver is already registered.</exception>
    public static IServiceCollection AddRaskTenant(this IServiceCollection services, Func<IServiceProvider, Guid?> resolve)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(resolve);

        if (services.Any(static d => d.ServiceType == typeof(TenantResolver)))
        {
            throw new InvalidOperationException(
                "A tenant resolver is already registered. AddRaskTenant is called once: two resolvers would " +
                "make which tenant a request belongs to depend on the order of two lines of startup.");
        }

        services.AddSingleton(new TenantResolver(resolve));
        services.AddScoped<ResolvedTenant>();

        return services;
    }

    /// <summary>
    ///     Says where the tenant of a request or a live session comes from, for an app that numbers its tenants.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="resolve">
    ///     Returns the tenant's number — the value its rows hold in <c>TenantId</c> — or
    ///     <see langword="null" /> when the work belongs to none. An <c>int?</c> is returned as it is.
    /// </param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    ///     Exactly <see cref="AddRaskTenant(IServiceCollection, Func{IServiceProvider, Guid?})" />, with the
    ///     number carried the way <see cref="Tenant.Use(long)" /> carries it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A resolver is already registered.</exception>
    public static IServiceCollection AddRaskTenant(this IServiceCollection services, Func<IServiceProvider, long?> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);

        return services.AddRaskTenant(sp => resolve(sp) is { } number ? TenantNumber.ToGuid(number) : (Guid?)null);
    }
}
