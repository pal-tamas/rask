using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Caching;

/// <summary>
///     The cache in a <c>TestDatabase</c>: its fake, so a save handler's <c>Cache.Remember</c> or <c>Cache.Forget</c>
///     works in memory rather than throwing for want of a host — <c>database.Cache.Forgot("products").Once()</c>.
/// </summary>
public static class CacheTestDatabase
{
    extension(TestDatabase database)
    {
        /// <summary>
        ///     The cache fake this database stands in for the battery with, once its model maps the cache table (a
        ///     RaskApp's context does): an in-memory cache that really stores, to ask about.
        /// </summary>
        /// <exception cref="InvalidOperationException">The model maps no cache table, or the test registered its own.</exception>
        public CacheFake Cache =>
            database.Fakes.Required<CacheFake>("cache", "modelBuilder.AddRaskCache()", "Cache.Fake()");
    }

    /// <summary>Runs when this assembly is first used — by a model that maps the cache table, before its test database wires.</summary>
    // CA2255 warns that a module initializer in a library surprises its consumer. Here it is the mechanism, as for the
    // outbox's (OutboxTestDatabase): Rask.Data cannot reference this assembly to ask for its fake.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register() => TestDatabaseBatteries.Add(new Battery());

    private sealed class Battery : ITestDatabaseBattery
    {
        public void Wire<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>(
            IServiceCollection services, IModel model, TestDatabaseFakes fakes)
            where TContext : DbContext
        {
            if (model.FindEntityType(typeof(CacheEntry)) is not null)
            {
                fakes.Use<ICache, CacheFake>(services, global::Rask.Caching.Cache.Faked.Value as CacheFake, static () => new CacheFake());
            }
        }
    }
}
