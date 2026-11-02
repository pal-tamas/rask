using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Providers.E2E.Tests;

/// <summary>
///     A serializable <c>Db.Transaction</c> — committed, or rolled back by a throw — and then a plain read that draws the
///     same pooled connection: the read runs at the database's default isolation, not the block's. SQL Server keeps a
///     <c>SET TRANSACTION ISOLATION LEVEL</c> on the session and the pool's reset does not clear it, so without Rask
///     putting it back every later read on that connection took range locks and lost read-committed snapshot.
/// </summary>
internal static class IsolationAfterRun
{
    /// <summary>The SQL that reads the connection's session id and its isolation level, as one provider spells them.</summary>
    internal sealed record Probe(string SessionId, string Isolation);

    /// <summary>The session the block ran on, and the session and isolation level of the read after it.</summary>
    internal static async Task<(string Inside, string After, string Level)> Run(
        Action<DbContextOptionsBuilder> provider, Probe probe, bool throws)
    {
        var services = new ServiceCollection();
        services.AddRaskData<TenancyDbContext>();
        services.AddDbContextFactory<TenancyDbContext>((sp, o) =>
        {
            provider(o);
            o.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
        });
        services.AddDbContextFactory<RaskReadDbContext>(provider);
        await using var app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(app);

        try
        {
            var factory = app.GetRequiredService<IDbContextFactory<TenancyDbContext>>();
            var inside = "";

            var block = Db.Transaction(async () =>
            {
                // The app's own context, joined at its first command: it runs on the block's connection.
                await using var db = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
                inside = await Scalar(db, probe.SessionId);
                await Member.Count(m => m.Owner == null, TestContext.Current.CancellationToken);
                if (throws)
                {
                    throw new InvalidOperationException("roll back");
                }
            }, IsolationLevel.Serializable, TestContext.Current.CancellationToken);
            if (throws)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => block);
            }
            else
            {
                await block;
            }

            await using var next = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
            await next.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            var after = await Scalar(next, probe.SessionId);
            var level = await Scalar(next, probe.Isolation);
            return (inside, after, level);
        }
        finally
        {
            Db.Reset();
        }
    }

    private static Task<string> Scalar(DbContext db, string sql) =>
#pragma warning disable EF1002 // every probe is a constant of this file
        db.Database.SqlQueryRaw<string>(sql).SingleAsync(TestContext.Current.CancellationToken);
#pragma warning restore EF1002
}

[Collection(PostgresCollection.Name)]
public sealed class PostgresDbTransactionIsolationResetTests
{
    private static readonly IsolationAfterRun.Probe Probe = new(
        """SELECT pg_backend_pid()::text AS "Value" """,
        """SELECT current_setting('transaction_isolation') AS "Value" """);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_read_after_a_serializable_block_on_its_pooled_connection_runs_read_committed(bool throws)
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using (var reset = new TenancyDbContext(
            new DbContextOptionsBuilder<TenancyDbContext>().UseRaskPostgresAt(Postgres.Required).Options))
        {
            await Postgres.ResetSchemaAsync(reset, "tenancy");
        }

        var (inside, after, level) = await IsolationAfterRun.Run(o => o.UseRaskPostgresAt(Postgres.Required), Probe, throws);

        Assert.Equal(inside, after);
        Assert.Equal("read committed", level);
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerDbTransactionIsolationResetTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_db_transaction_isolation_reset";

    // sys.dm_exec_sessions: 2 is read committed, 4 serializable.
    private static readonly IsolationAfterRun.Probe Probe = new(
        "SELECT CAST(@@SPID AS nvarchar(10)) AS Value",
        "SELECT CAST(transaction_isolation_level AS nvarchar(10)) AS Value FROM sys.dm_exec_sessions WHERE session_id = @@SPID");

    public async ValueTask InitializeAsync()
    {
        if (SqlServer.Available)
        {
            await using var db = Open();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (SqlServer.Available)
        {
            await using var db = Open();
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_read_after_a_serializable_block_on_its_pooled_connection_runs_read_committed(bool throws)
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);

        var (inside, after, level) = await IsolationAfterRun.Run(
            o => o.UseRaskSqlServerAt(SqlServer.Database(DatabaseName)), Probe, throws);

        // The same session, drawn from the pool again: what the block set would still be on it.
        Assert.Equal(inside, after);
        Assert.Equal("2", level);
    }

    [Fact]
    public async Task The_next_open_after_a_hand_written_serializable_transaction_runs_read_committed()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        string inside;

        // Retrying off: a retrying strategy refuses a transaction begun outside it.
        await using (var db = Open(retry: false))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, TestContext.Current.CancellationToken);
            inside = await Scalar(db, Probe.SessionId);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        await using var next = Open();
        await next.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var after = await Scalar(next, Probe.SessionId);
        var level = await Scalar(next, Probe.Isolation);

        // The same session back from the pool, put back to read committed by the settings sent on every open.
        Assert.Equal(inside, after);
        Assert.Equal("2", level);
    }

    private static Task<string> Scalar(DbContext db, string sql) =>
#pragma warning disable EF1002 // every probe is a constant of this file
        db.Database.SqlQueryRaw<string>(sql).SingleAsync(TestContext.Current.CancellationToken);
#pragma warning restore EF1002

    private static TenancyDbContext Open(bool retry = true) =>
        new(new DbContextOptionsBuilder<TenancyDbContext>()
            .UseRaskSqlServerAt(SqlServer.Database(DatabaseName), s => s.Retry.Enabled = retry)
            .Options);
}
