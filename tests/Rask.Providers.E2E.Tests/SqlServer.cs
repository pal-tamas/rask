using Microsoft.Data.SqlClient;

namespace Rask.Providers.E2E.Tests;

/// <summary>The SQL Server under test: <c>RASK_MSSQL_TEST_DB</c>, or the run's container (<see cref="ProviderServer" />).</summary>
/// <remarks>
/// Each test class works in a database of its own, created and dropped by EF — no hand-written DDL, and no
/// <c>EnsureDeleted</c> that could ever point at a shared database, because the name is always one of ours.
/// </remarks>
internal static class SqlServer
{
    internal static string SkipReason => ProviderServer.SqlServer.SkipReason ?? "";

    internal static bool Available => ProviderServer.SqlServer.Available;

    /// <summary>The server's connection string, at <c>master</c>; the container is started on the first call.</summary>
    internal static string ConnectionString => ProviderServer.SqlServer.ConnectionString;

    /// <summary>The server's connection string, pointed at <paramref name="database"/>.</summary>
    internal static string Database(string database) =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = database }.ConnectionString;
}

/// <summary>Every SQL Server class runs alone: they share one server, and the races are the point.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerCollection
{
    public const string Name = "sqlserver";
}
