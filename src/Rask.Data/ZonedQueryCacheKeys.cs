using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Data;

/// <summary>
///     The provider's compiled-query cache key with the app's zone (<see cref="Clock.Zone" />) added: a query that reads a
///     moment's day (<c>f.At.Day</c>, on SQL Server and PostgreSQL) has the zone written into its SQL, so a query run again after the
///     zone changed — a test under its own zone, an app that names its zone after a first query — is compiled again for
///     the new zone instead of reusing the old one's SQL.
/// </summary>
/// <remarks>
///     EF keys the cache by the query's LINQ shape and asks a member translator nothing at run time, so the zone cannot be
///     a parameter of the SQL; it is a part of the key instead. A query run under one zone, as an app's always are, is
///     compiled once, as before. Linked into Rask.SqlServer, which does not reference this assembly.
/// </remarks>
/// <param name="provider">The provider's own key generator.</param>
/// <typeparam name="TProvider">The provider's own key generator's type.</typeparam>
internal sealed class ZonedQueryCacheKeys<TProvider>(TProvider provider) : ICompiledQueryCacheKeyGenerator
    where TProvider : ICompiledQueryCacheKeyGenerator
{
    // S2306: 'async' is EF's own name for the parameter, which S927 asks an implementation to keep.
#pragma warning disable S2306
    public object GenerateCacheKey(Expression query, bool async) =>
        new ZonedKey(provider.GenerateCacheKey(query, async), Clock.Zone.Id);
#pragma warning restore S2306

    // The zone by its id: an ordinal string comparison, where TimeZoneInfo's own equality also compares every rule.
    private sealed record ZonedKey(object Provider, string Zone);
}

/// <summary>Registers <see cref="ZonedQueryCacheKeys{TProvider}" /> over the provider's own key generator.</summary>
internal static class ZonedQueryCacheKeys
{
    /// <summary>
    ///     Puts the zone into every compiled-query cache key of the context's provider.
    /// </summary>
    /// <remarks>
    ///     Added, not tried: the provider registers its own generator before a Rask extension's services are applied,
    ///     and the last registration wins. The provider's generator is registered by its type too, which is how the
    ///     decorator is handed it.
    /// </remarks>
    /// <param name="services">EF's internal services.</param>
    /// <typeparam name="TProvider">The provider's own key generator's type.</typeparam>
    internal static void Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
        IServiceCollection services)
        where TProvider : class, ICompiledQueryCacheKeyGenerator
    {
        services.TryAddScoped<TProvider>();
        services.AddScoped<ICompiledQueryCacheKeyGenerator, ZonedQueryCacheKeys<TProvider>>();
    }
}
