using Microsoft.Data.SqlClient;

namespace Rask.Providers.E2E.Tests;

/// <summary>The SQL Server under test: the run's container (<see cref="ProviderServer" />).</summary>
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

    /// <summary>Drops <paramref name="database"/> if it is there, creates it empty and runs each batch in it, in order.</summary>
    /// <remarks>For a schema built by hand — the shape another tool made — rather than by EF.</remarks>
    internal static async Task RecreateDatabaseAsync(string database, params string[] batches)
    {
        await DropDatabaseAsync(database);
        await using (var master = new SqlConnection(Database("master")))
        {
            await master.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{database}]", master) { CommandTimeout = 300 };
            await create.ExecuteNonQueryAsync();
        }

        await using var connection = new SqlConnection(Database(database));
        await connection.OpenAsync();
        foreach (var batch in batches)
        {
            // Schema work on a busy shared runner outlasts the 30-second default.
            await using var command = new SqlCommand(batch, connection) { CommandTimeout = 300 };
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Drops <paramref name="database"/>, closing whatever still holds it open; nothing when it is not there.</summary>
    internal static async Task DropDatabaseAsync(string database)
    {
        await using var master = new SqlConnection(Database("master"));
        await master.OpenAsync();
        await using var drop = new SqlCommand(
            $"IF DB_ID('{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END",
            master)
        {
            CommandTimeout = 300,
        };
        await drop.ExecuteNonQueryAsync();
    }
}

/// <summary>Every SQL Server class runs alone: they share one server, and the races are the point.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerCollection
{
    public const string Name = "sqlserver";
}
