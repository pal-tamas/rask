using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rask.Postgres;
using Rask.SQLite;
using Rask.SqlServer;

namespace Rask;

/// <summary>Reads <c>Rask:Database:Provider</c>, the one setting that decides which database an app opens.</summary>
internal static class RaskDatabase
{
    internal const string ProviderKey = "Rask:Database:Provider";

    /// <summary>
    /// The provider <paramref name="configuration"/> names — SQLite when it names none, so an app that never set the
    /// key keeps the database it always had.
    /// </summary>
    internal static RaskDatabaseProvider Provider(IConfiguration? configuration)
    {
        var value = configuration?[ProviderKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return RaskDatabaseProvider.Sqlite;
        }

        foreach (var provider in (ReadOnlySpan<RaskDatabaseProvider>)
                 [RaskDatabaseProvider.Sqlite, RaskDatabaseProvider.Postgres, RaskDatabaseProvider.SqlServer])
        {
            if (string.Equals(value.Trim(), Name(provider), StringComparison.OrdinalIgnoreCase))
            {
                return provider;
            }
        }

        throw new InvalidOperationException(
            $"{ProviderKey} is '{value}', which is not a database Rask can open. Set it to sqlite, postgres or sqlserver "
            + "(Rask__Database__Provider in the environment), or remove it to keep SQLite.");
    }

    /// <summary>The value <c>Rask:Database:Provider</c> spells <paramref name="provider"/> with.</summary>
    internal static string Name(RaskDatabaseProvider provider) => provider switch
    {
        RaskDatabaseProvider.Sqlite => "sqlite",
        RaskDatabaseProvider.Postgres => "postgres",
        RaskDatabaseProvider.SqlServer => "sqlserver",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    /// <summary>What EF Core reports as <c>Database.ProviderName</c> for <paramref name="provider"/>.</summary>
    internal static string EfProviderName(RaskDatabaseProvider provider) => provider switch
    {
        RaskDatabaseProvider.Sqlite => "Microsoft.EntityFrameworkCore.Sqlite",
        RaskDatabaseProvider.Postgres => "Npgsql.EntityFrameworkCore.PostgreSQL",
        RaskDatabaseProvider.SqlServer => "Microsoft.EntityFrameworkCore.SqlServer",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };
}
