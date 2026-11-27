using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     A database provider package the server host finds by name rather than references: Rask.Postgres, a separate
///     package that only a PostgreSQL app adds.
/// </summary>
/// <remarks>
///     Rask.Server does not depend on it, so an app on SQLite or SQL Server never restores Npgsql. The host names the
///     provider's entry type as a constant and asks for it at run time; an app that added the package has it, one that
///     did not is told so.
/// </remarks>
internal interface IRaskDatabaseProvider
{
    /// <summary>Configures <paramref name="optionsBuilder" /> for this database, as the provider's own <c>UseRask…</c> does.</summary>
    /// <param name="optionsBuilder">The context options builder being configured.</param>
    /// <param name="services">The application's service provider.</param>
    /// <returns>The same builder.</returns>
    DbContextOptionsBuilder Use(DbContextOptionsBuilder optionsBuilder, IServiceProvider services);
}
