using System.Data.Common;

namespace Rask.Data;

/// <summary>
///     The connection string of a test's own database: the one the environment names, with a unique suffix on the
///     database's name — or, on SQLite, on its file.
/// </summary>
internal static class PerTestConnectionString
{
    private const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";

    // SQL Server spells it either way; PostgreSQL, MySQL and most others say Database.
    private static readonly string[] DatabaseKeys = ["Initial Catalog", "Database"];

    private static readonly string[] SqliteFileKeys = ["Data Source", "DataSource", "Filename"];

    /// <summary>The connection string of a test's own database, beside the one <paramref name="connectionString" /> names.</summary>
    /// <param name="connectionString">The connection string the environment variable holds.</param>
    /// <param name="providerName">The EF Core provider the app configured for it.</param>
    /// <param name="suffix">What makes the name unique to this test.</param>
    public static string For(string connectionString, string? providerName, string suffix)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        return string.Equals(providerName, SqliteProvider, StringComparison.Ordinal)
            ? SqliteFile(builder, suffix)
            : ServerDatabase(builder, suffix);
    }

    private static string ServerDatabase(DbConnectionStringBuilder builder, string suffix)
    {
        foreach (var key in DatabaseKeys)
        {
            if (builder.TryGetValue(key, out var value) && value is string { Length: > 0 } name)
            {
                builder[key] = $"{name}_{suffix}";
                return builder.ConnectionString;
            }
        }

        builder["Database"] = $"rask_test_{suffix}";
        return builder.ConnectionString;
    }

    private static string SqliteFile(DbConnectionStringBuilder builder, string suffix)
    {
        var inMemory = builder.TryGetValue("Mode", out var mode)
                       && string.Equals(mode as string, "Memory", StringComparison.OrdinalIgnoreCase);
        foreach (var key in SqliteFileKeys)
        {
            if (!builder.TryGetValue(key, out var value) || value is not string { Length: > 0 } path)
            {
                continue;
            }

            // An in-memory database is already the test's own: it lives on one connection and nobody else's.
            if (inMemory || string.Equals(path, ":memory:", StringComparison.Ordinal))
            {
                return builder.ConnectionString;
            }

            var file = $"{Path.GetFileNameWithoutExtension(path)}-{suffix}{Path.GetExtension(path)}";
            builder[key] = Path.Combine(Path.GetDirectoryName(path) ?? "", file);
            return builder.ConnectionString;
        }

        return builder.ConnectionString;
    }
}
