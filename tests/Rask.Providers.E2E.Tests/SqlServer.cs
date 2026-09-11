using Microsoft.Data.SqlClient;

namespace Rask.Providers.E2E.Tests;

/// <summary>The SQL Server under test, named by <c>RASK_MSSQL_TEST_DB</c>.</summary>
/// <remarks>
/// Each test class works in a database of its own, created and dropped by EF — no hand-written DDL, and no
/// <c>EnsureDeleted</c> that could ever point at a shared database, because the name is always one of ours.
/// </remarks>
internal static class SqlServer
{
    internal const string SkipReason =
        "Needs a SQL Server: run scripts/run-providers-local.sh on an amd64 host, or set RASK_MSSQL_TEST_DB.";

    internal static string? ConnectionString => Environment.GetEnvironmentVariable("RASK_MSSQL_TEST_DB");

    internal static bool Available => !string.IsNullOrWhiteSpace(ConnectionString);

    /// <summary>The server's connection string, pointed at <paramref name="database"/>.</summary>
    internal static string Database(string database) =>
        new SqlConnectionStringBuilder(ConnectionString ?? throw new InvalidOperationException("RASK_MSSQL_TEST_DB is not set."))
        {
            InitialCatalog = database,
        }.ConnectionString;
}

/// <summary>Every SQL Server class runs alone: they share one server, and the races are the point.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerCollection
{
    public const string Name = "sqlserver";
}
