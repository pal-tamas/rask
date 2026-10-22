using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
///     Raw SQL an app runs on its own context inside <c>Db.Transaction</c> — <c>ExecuteSqlRaw</c>, <c>SqlQueryRaw</c> —
///     runs inside the transaction, and a later <c>db:</c> call with the same context joins it as well.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class DbTransactionRawSqlTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-raw-sql-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task A_raw_write_before_a_db_call_runs_inside_and_the_db_call_joins()
    {
        await using var database = await StartDatabaseAsync();
        await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
        await using var mine = Db.CreateOwnContext();

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("tongs"), cancellationToken: TestContext.Current.CancellationToken);
            await mine.Database.ExecuteSqlRawAsync("UPDATE Widget SET Name = 'renamed' WHERE Name = 'anvil'", TestContext.Current.CancellationToken);
            await GeneratedModelWrites.Create(Widget.Create("hammer"), db: mine, cancellationToken: TestContext.Current.CancellationToken);
            await mine.SaveChangesAsync(TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["hammer", "renamed", "tongs"], await Names());
    }

    [Fact]
    public async Task A_raw_read_before_a_db_call_sees_the_work_so_far()
    {
        await using var database = await StartDatabaseAsync();
        await using var mine = Db.CreateOwnContext();
        var seen = -1;

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
            seen = await mine.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Widget").SingleAsync(TestContext.Current.CancellationToken);
            await GeneratedModelWrites.Create(Widget.Create("hammer"), db: mine, cancellationToken: TestContext.Current.CancellationToken);
            await mine.SaveChangesAsync(TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, seen);
        Assert.Equal(["anvil", "hammer"], await Names());
    }

    [Fact]
    public async Task A_raw_query_of_entities_before_a_db_call_sees_the_work_so_far()
    {
        await using var database = await StartDatabaseAsync();
        await using var mine = Db.CreateOwnContext();
        List<string> seen = [];

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
            seen = await mine.Set<Widget>().FromSqlRaw("SELECT * FROM Widget").Select(w => w.Name).ToListAsync(TestContext.Current.CancellationToken);
            await GeneratedModelWrites.Create(Widget.Create("hammer"), db: mine, cancellationToken: TestContext.Current.CancellationToken);
            await mine.SaveChangesAsync(TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["anvil"], seen);
        Assert.Equal(["anvil", "hammer"], await Names());
    }

    [Fact]
    public async Task A_raw_write_after_a_db_call_runs_inside_too()
    {
        await using var database = await StartDatabaseAsync();
        await using var mine = Db.CreateOwnContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), db: mine, cancellationToken: TestContext.Current.CancellationToken);
            await mine.SaveChangesAsync(TestContext.Current.CancellationToken);
            await mine.Database.ExecuteSqlRawAsync("UPDATE Widget SET Name = 'renamed'", TestContext.Current.CancellationToken);
            throw new InvalidOperationException("roll back");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(await Names());
    }

    [Fact]
    public async Task A_rollback_takes_a_raw_write_back()
    {
        await using var database = await StartDatabaseAsync();
        await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
        await using var mine = Db.CreateOwnContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("tongs"), cancellationToken: TestContext.Current.CancellationToken);
            await mine.Database.ExecuteSqlRawAsync("UPDATE Widget SET Name = 'renamed' WHERE Name = 'anvil'", TestContext.Current.CancellationToken);
            throw new InvalidOperationException("roll back");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(["anvil"], await Names());
    }

    [Fact]
    public async Task A_connection_opened_by_hand_inside_joins_and_closes_without_ending_the_work()
    {
        await using var database = await StartDatabaseAsync();
        await using var mine = Db.CreateOwnContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await mine.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            await mine.Database.ExecuteSqlRawAsync("DELETE FROM Widget", TestContext.Current.CancellationToken);
            await mine.Database.CloseConnectionAsync();
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
            throw new InvalidOperationException("roll back");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(await Names());
    }

    [Fact]
    public async Task A_transaction_begun_by_hand_inside_is_refused_rather_than_nested()
    {
        await using var database = await StartDatabaseAsync();
        await using var mine = Db.CreateOwnContext();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await using var own = await mine.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("already in a transaction", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_context_on_another_database_runs_its_raw_sql_there_and_its_db_call_is_refused()
    {
        await using var database = await StartDatabaseAsync();
        var otherPath = Path.Combine(Path.GetTempPath(), $"rask-raw-sql-other-{Guid.NewGuid():N}.db");
        await using var other = new RaskDbContext(new DbContextOptionsBuilder<RaskDbContext>().UseSqlite($"Data Source={otherPath}").Options);
        await other.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await other.Database.ExecuteSqlRawAsync("DELETE FROM Widget", TestContext.Current.CancellationToken);
            await GeneratedModelWrites.Create(Widget.Create("elsewhere"), db: other, cancellationToken: TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("another database", refused.Message, StringComparison.Ordinal);
        await other.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(otherPath);
    }

    [Fact]
    public async Task A_context_that_ran_raw_sql_inside_is_handed_back_on_its_own_connection()
    {
        await using var database = await StartDatabaseAsync();
        await using var mine = Db.CreateOwnContext();
        var connectionString = mine.Database.GetConnectionString();
        System.Data.Common.DbConnection? joined = null;

        await Db.Transaction(async () =>
        {
            await mine.Database.ExecuteSqlRawAsync("DELETE FROM Widget", TestContext.Current.CancellationToken);
            joined = mine.Database.GetDbConnection();
        }, cancellationToken: TestContext.Current.CancellationToken);
        mine.Add(Widget.Create("hammer"));
        await mine.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Null(mine.Database.CurrentTransaction);
        Assert.NotSame(joined, mine.Database.GetDbConnection());
        Assert.Equal(connectionString, mine.Database.GetConnectionString());
        Assert.Equal(System.Data.ConnectionState.Closed, mine.Database.GetDbConnection().State);
        Assert.Equal(["hammer"], await Names());
    }

    [Fact]
    public async Task Raw_sql_outside_any_transaction_runs_on_the_contexts_own_connection()
    {
        await using var database = await StartDatabaseAsync();
        await using var mine = Db.CreateOwnContext();
        var own = mine.Database.GetDbConnection();

        await mine.Database.ExecuteSqlRawAsync("DELETE FROM Widget", TestContext.Current.CancellationToken);

        Assert.Same(own, mine.Database.GetDbConnection());
        Assert.Null(mine.Database.CurrentTransaction);
    }

    private static async Task<List<string>> Names()
    {
        await using var read = Db.CreateOwnContext();
        var names = await read.Set<Widget>().Select(w => w.Name).ToListAsync(TestContext.Current.CancellationToken);
        return [.. names.Order(StringComparer.Ordinal)];
    }

    // A short busy timeout, so a raw write left outside fails fast on the lock the transaction holds, not in 30 s.
    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath};Default Timeout=2"), cancellationToken: TestContext.Current.CancellationToken);
}
