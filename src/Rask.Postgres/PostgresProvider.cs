using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Postgres;

/// <summary>
///     Created by name by Rask.Server's <c>UseRaskDatabase</c> when <c>Rask:Database:Provider</c> is <c>postgres</c>
///     (its <c>PostgresLoader</c>): how an app that added this package opens PostgreSQL with no code of its own.
/// </summary>
internal sealed class PostgresProvider : IRaskDatabaseProvider
{
    public DbContextOptionsBuilder Use(DbContextOptionsBuilder optionsBuilder, IServiceProvider services) =>
        optionsBuilder.UseRaskPostgres(services);
}
