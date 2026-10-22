using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

/// <summary>
///     An app's own context on <c>UseRaskSqlite</c>, made before <c>Db.Transaction</c>, joins it at its first command —
///     and joining sends no pragmas: the transaction's connection had them when it opened, and <c>journal_mode</c> or
///     <c>synchronous</c> inside a transaction is an error.
/// </summary>
public sealed class RaskSqliteDbTransactionJoinTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-sqlite-join-{Guid.NewGuid():N}.db");
    private ServiceProvider _app = null!;

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddRaskData<ProbeDbContext>();
        services.AddDbContextFactory<ProbeDbContext>((sp, o) => o
            .UseRaskSqliteAt($"Data Source={_dbPath}", p => p.Synchronous = SqliteSynchronous.Normal)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseRaskSqliteAt($"Data Source={_dbPath}"));
        _app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(_app);

        await using var schema = await Contexts.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await schema.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        await _app.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-shm", $"{_dbPath}-wal" })
        {
            File.Delete(path);
        }
    }

    private IDbContextFactory<ProbeDbContext> Contexts => _app.GetRequiredService<IDbContextFactory<ProbeDbContext>>();

    [Fact]
    public async Task A_context_made_before_the_block_reads_and_saves_inside_it()
    {
        await using var db = await Contexts.CreateDbContextAsync(TestContext.Current.CancellationToken);

        await Db.Transaction(async () =>
        {
            await db.Rows.CountAsync(TestContext.Current.CancellationToken);
            db.Rows.Add(new ProbeRow { Name = "anvil" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, await Count());
    }

    [Fact]
    public async Task A_throw_after_the_save_takes_it_back()
    {
        await using var db = await Contexts.CreateDbContextAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await db.Rows.CountAsync(TestContext.Current.CancellationToken);
            db.Rows.Add(new ProbeRow { Name = "anvil" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            throw new InvalidOperationException("roll back");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(0, await Count());
    }

    [Fact]
    public async Task A_context_that_joins_as_it_opens_sends_no_pragmas_onto_the_transaction_s_connection()
    {
        await using var first = await Contexts.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await using var second = await Contexts.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var seen = -1;

        await Db.Transaction(async () =>
        {
            await first.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 1234", TestContext.Current.CancellationToken);
            seen = await second.Database.SqlQueryRaw<int>("SELECT timeout AS Value FROM pragma_busy_timeout")
                .SingleAsync(TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1234, seen);
    }

    [Fact]
    public async Task Handed_back_its_own_connection_gets_the_pragmas_again_when_it_opens()
    {
        await using var db = await Contexts.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await Db.Transaction(
            () => db.Rows.CountAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);

        await db.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var busyTimeout = Pragma((SqliteConnection)db.Database.GetDbConnection(), "busy_timeout");
        await db.Database.CloseConnectionAsync();

        Assert.Equal("5000", busyTimeout);
    }

    private async Task<int> Count()
    {
        await using var check = await Contexts.CreateDbContextAsync(TestContext.Current.CancellationToken);
        return await check.Rows.CountAsync(TestContext.Current.CancellationToken);
    }

    private static string Pragma(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options)
    {
        public DbSet<ProbeRow> Rows => Set<ProbeRow>();
    }

    private sealed class ProbeRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }
}
