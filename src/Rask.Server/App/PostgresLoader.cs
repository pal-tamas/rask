using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask;

/// <summary>Opens PostgreSQL through the Rask.Postgres package the app added, found by name.</summary>
/// <remarks>
///     <para>
///         Rask.Postgres is a separate package that only a PostgreSQL app adds, so an app on SQLite or SQL Server never
///         restores Npgsql. Rask.Server does not reference it: as with Rask.Pdf (<c>PdfLoader</c>), the host asks for the
///         provider's entry type BY NAME. The name is a constant, so the trimmer resolves it and keeps the type.
///     </para>
/// </remarks>
internal static class PostgresLoader
{
    /// <summary>The provider's assembly-qualified name.</summary>
    internal const string ProviderTypeName = "Rask.Postgres.PostgresProvider, Rask.Postgres";

    /// <summary>The one sentence an app configured for PostgreSQL without the package is told.</summary>
    internal const string MissingPackage =
        RaskDatabase.ProviderKey + " is postgres, but this app does not reference Rask.Postgres: add the Rask.Postgres package.";

    /// <summary>Configures <paramref name="optionsBuilder" /> for PostgreSQL, or says which package is missing.</summary>
    internal static DbContextOptionsBuilder Use(DbContextOptionsBuilder optionsBuilder, IServiceProvider services)
    {
        var type = Type.GetType(ProviderTypeName, throwOnError: false);
        if (type is null || Activator.CreateInstance(type) is not IRaskDatabaseProvider provider)
        {
            throw new InvalidOperationException(MissingPackage);
        }

        return provider.Use(optionsBuilder, services);
    }
}
