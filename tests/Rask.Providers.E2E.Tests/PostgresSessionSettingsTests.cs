using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>
/// The session settings actually reach the server, and survive the pool.
/// </summary>
/// <remarks>
/// The unit tests prove the connection string <c>UseRaskPostgres</c> builds. Only a server proves the settings
/// land: Npgsql resets a returned connection with <c>DISCARD ALL</c>, so a <c>SET</c> issued after opening would
/// be gone by the second request — the startup parameters are what that reset restores to. And they must reach
/// a connection opened without EF, which is how bulk insert's writer and hand-written ADO code open one.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PostgresSessionSettingsTests
{
    [SkippableFact]
    public async Task Every_opened_connection_carries_the_configured_timeouts()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);

        var options = new DbContextOptionsBuilder<SettingsContext>()
            .UseRaskPostgresAt(Postgres.Required, o =>
            {
                o.StatementTimeout = TimeSpan.FromSeconds(42);
                o.LockTimeout = TimeSpan.FromSeconds(7);
                o.IdleInTransactionSessionTimeout = TimeSpan.FromSeconds(90);
            })
            .Options;

        // The second pass takes the same physical connection back out of the pool, after its reset.
        for (var pass = 0; pass < 2; pass++)
        {
            await using var db = new SettingsContext(options);
            await db.Database.OpenConnectionAsync();

            await AssertTimeoutsAsync(db.Database.GetDbConnection());

            // A SET inside the session must not outlive it: the pool's reset restores the startup value.
            await ExecuteAsync(db.Database.GetDbConnection(), "SET statement_timeout = 1");
            await db.Database.CloseConnectionAsync();
        }

        // Opened directly on the DbConnection, with no EF open in the path at all.
        await using var raw = new SettingsContext(options);
        var connection = raw.Database.GetDbConnection();
        await connection.OpenAsync();
        await AssertTimeoutsAsync(connection);
        await connection.CloseAsync();
    }

    private static async Task AssertTimeoutsAsync(System.Data.Common.DbConnection connection)
    {
        Assert.Equal("42s", await ShowAsync(connection, "statement_timeout"));
        Assert.Equal("7s", await ShowAsync(connection, "lock_timeout"));
        Assert.Equal("90s", await ShowAsync(connection, "idle_in_transaction_session_timeout"));
    }

    private static async Task ExecuteAsync(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ShowAsync(System.Data.Common.DbConnection connection, string setting)
    {
        await using var command = connection.CreateCommand();

        // SHOW takes an identifier, not a parameter; every caller passes a constant.
        command.CommandText = $"SHOW {setting}";
        return (string?)await command.ExecuteScalarAsync();
    }

    private sealed class SettingsContext(DbContextOptions<SettingsContext> options) : DbContext(options);
}
