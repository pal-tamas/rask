using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data.Tests;

/// <summary>
///     A context handed into <c>Db.Transaction</c> as <c>db:</c> is the caller's: once the transaction ends — commit,
///     rollback or retry — it is back on its own connection, in no transaction, exactly as it was handed in.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class DbTransactionHandsBackContextTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-hand-back-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task One_context_runs_two_transactions_and_then_a_save_outside_any()
    {
        await using var database = await StartDatabaseAsync();
        var db = database.Context;
        var anvil = Widget.Create("anvil");

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(anvil, db: db, cancellationToken: TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);
        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Delete<Widget>(anvil.Id, version: null, db: db, cancellationToken: TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);
        db.Add(Widget.Create("hammer"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(["hammer"], await Names());
    }

    [Fact]
    public async Task A_context_reused_after_a_rollback_saves_on_its_own_and_joins_the_next_one()
    {
        await using var database = await StartDatabaseAsync();
        var db = database.Context;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("staged"), db: db, cancellationToken: TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            throw new InvalidOperationException("roll back");
        }, cancellationToken: TestContext.Current.CancellationToken));
        db.ChangeTracker.Clear();
        db.Add(Widget.Create("outside"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("inside"), db: db, cancellationToken: TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(["inside", "outside"], await Names());
    }

    [Fact]
    public async Task A_context_admitted_twice_in_one_transaction_is_handed_back_once()
    {
        await using var database = await StartDatabaseAsync();
        await using var mine = Db.CreateOwnContext();

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), db: mine, cancellationToken: TestContext.Current.CancellationToken);
            await GeneratedModelWrites.Create(Widget.Create("hammer"), db: mine, cancellationToken: TestContext.Current.CancellationToken);
            await mine.SaveChangesAsync(TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);
        mine.Add(Widget.Create("tongs"));
        await mine.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Null(mine.Database.CurrentTransaction);
        Assert.Equal(["anvil", "hammer", "tongs"], await Names());
    }

    [Fact]
    public async Task A_context_that_never_opened_a_connection_opens_its_own_again()
    {
        await using var database = await StartDatabaseAsync();
        await using var mine = Db.CreateOwnContext();
        var connectionString = mine.Database.GetConnectionString();
        System.Data.Common.DbConnection? joined = null;

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), db: mine, cancellationToken: TestContext.Current.CancellationToken);
            joined = mine.Database.GetDbConnection();
            await mine.SaveChangesAsync(TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);
        mine.Add(Widget.Create("hammer"));
        await mine.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.NotSame(joined, mine.Database.GetDbConnection());
        Assert.Equal(connectionString, mine.Database.GetConnectionString());
        Assert.Equal(System.Data.ConnectionState.Closed, mine.Database.GetDbConnection().State);
        Assert.Equal(["anvil", "hammer"], await Names());
    }

    [Fact]
    public async Task A_context_on_a_connection_the_caller_supplied_gets_it_back_undisposed()
    {
        await using var database = await StartDatabaseAsync();
        await using var callers = new SqliteConnection($"Data Source={_dbPath}");
        var mine = new RaskDbContext(new DbContextOptionsBuilder<RaskDbContext>().UseSqlite(callers).Options);

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), db: mine, cancellationToken: TestContext.Current.CancellationToken);
            await mine.SaveChangesAsync(TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);
        var handedBack = mine.Database.GetDbConnection();
        await mine.DisposeAsync();
        await callers.OpenAsync(TestContext.Current.CancellationToken);

        Assert.Same(callers, handedBack);
        Assert.Equal(System.Data.ConnectionState.Open, callers.State);
        Assert.Equal(["anvil"], await Names());
    }

    [Fact]
    public async Task A_context_on_a_connection_the_caller_opened_keeps_it_open_and_its_own()
    {
        await using var callers = new SqliteConnection($"Data Source={_dbPath}");
        await callers.OpenAsync(TestContext.Current.CancellationToken);
        await using var database = await TestDatabase.Start(o => o.UseSqlite(callers), cancellationToken: TestContext.Current.CancellationToken);
        var mine = Db.CreateOwnContext();

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), db: mine, cancellationToken: TestContext.Current.CancellationToken);
            await mine.SaveChangesAsync(TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);
        var inTransaction = mine.Database.CurrentTransaction;
        var handedBack = mine.Database.GetDbConnection();
        await mine.DisposeAsync();

        Assert.Null(inTransaction);
        Assert.Same(callers, handedBack);
        Assert.Equal(System.Data.ConnectionState.Open, callers.State);
        Assert.Equal(["anvil"], await Names());
    }

    [Fact]
    public async Task A_retried_transaction_admits_the_same_context_again()
    {
        await using var database = await TestDatabase.Start(o => o.UseSqlite(
            $"Data Source={_dbPath}", s => s.ExecutionStrategy(d => new RetryOnTimeout(d))), cancellationToken: TestContext.Current.CancellationToken);
        await using var mine = Db.CreateOwnContext();
        var attempts = 0;

        await Db.Transaction(async () =>
        {
            attempts++;
            await GeneratedModelWrites.Create(Widget.Create($"attempt-{attempts}"), db: mine, cancellationToken: TestContext.Current.CancellationToken);
            await mine.SaveChangesAsync(TestContext.Current.CancellationToken);
            if (attempts == 1)
            {
                throw new TimeoutException("transient");
            }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        Assert.Null(mine.Database.CurrentTransaction);
        Assert.Equal(["attempt-2"], await Names());
    }

    private static async Task<List<string>> Names()
    {
        await using var read = Db.CreateOwnContext();
        var names = await read.Set<Widget>().Select(w => w.Name).ToListAsync(TestContext.Current.CancellationToken);
        return [.. names.Order(StringComparer.Ordinal)];
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"), cancellationToken: TestContext.Current.CancellationToken);

    private sealed class RetryOnTimeout(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 2, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TimeoutException;
    }
}
