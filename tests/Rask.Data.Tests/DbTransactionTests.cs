using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data.Tests;

/// <summary>
///     <c>Db.Transaction</c>: every read and write the work makes without a <c>db:</c> runs on one connection, in one
///     transaction, committed when the work completes and rolled back when it throws.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class DbTransactionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-transaction-{Guid.NewGuid():N}.db");
    private readonly IsolationRecorder _isolation = new();

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task The_writes_inside_commit_together_when_the_work_completes()
    {
        await using var database = await StartDatabaseAsync();

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
            await GeneratedModelWrites.Create(Widget.Create("hammer"), cancellationToken: TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, await Widget.Count(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_throw_rolls_every_write_back_and_reaches_the_caller()
    {
        await using var database = await StartDatabaseAsync();
        var thrown = new InvalidOperationException("out of stock");

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
            throw thrown;
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(thrown, caught);
        Assert.Equal(0, await Widget.Count(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_read_inside_sees_the_work_so_far_and_nobody_outside_does()
    {
        await using var database = await StartDatabaseAsync();
        var supplier = Supplier.In("HU");
        database.Context.Add(supplier);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        int inside = -1, outside = -1;

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Consignment.For(supplier.Id, "C-1", 10m), cancellationToken: TestContext.Current.CancellationToken);

            // The read face, on the read context: the check the save depends on, in the same transaction.
            inside = await Consignment.Count(TestContext.Current.CancellationToken);
            await using var other = database.OpenRead();
            outside = await other.Set<ConsignmentRead>().CountAsync(TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((1, 0), (inside, outside));
    }

    [Fact]
    public async Task The_work_can_hand_back_a_value()
    {
        await using var database = await StartDatabaseAsync();

        var id = await Db.Transaction(async () =>
            (await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken)).Id,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("anvil", (await database.Load<Widget>(id, TestContext.Current.CancellationToken))!.Name);
    }

    [Fact]
    public async Task A_find_then_save_inside_writes_in_the_same_transaction()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            var found = await GeneratedModelWrites.Find<Widget>(widget.Id, cancellationToken: TestContext.Current.CancellationToken);
            found!.Rename("renamed");
            await AggregateSave.SaveAsync(found, TestContext.Current.CancellationToken);
            throw new InvalidOperationException("changed my mind");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("anvil", (await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken))!.Name);
    }

    [Fact]
    public async Task Undo_queued_inside_runs_after_a_rollback_and_never_after_a_commit()
    {
        await using var database = await StartDatabaseAsync();
        var undone = new List<string>();

        await Db.Transaction(() => Queue(undone, "committed"), cancellationToken: TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await Queue(undone, "rolled back");
            throw new InvalidOperationException("the work failed");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(["rolled back"], undone);
    }

    [Fact]
    public async Task A_nested_transaction_joins_the_outer_one_and_rolls_back_with_it()
    {
        await using var database = await StartDatabaseAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await Db.Transaction(
                () => GeneratedModelWrites.Create(Widget.Create("inner"), cancellationToken: TestContext.Current.CancellationToken),
                cancellationToken: TestContext.Current.CancellationToken);
            throw new InvalidOperationException("outer fails after the inner completed");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(0, await Widget.Count(TestContext.Current.CancellationToken));
        Assert.Single(_isolation.Started);
    }

    [Fact]
    public async Task A_nested_transaction_asking_another_isolation_is_refused()
    {
        await using var database = await StartDatabaseAsync();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(
            () => Db.Transaction(() => Task.CompletedTask, IsolationLevel.Serializable, TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Serializable", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_nested_transaction_asking_no_isolation_or_the_same_one_joins()
    {
        await using var database = await StartDatabaseAsync();

        await Db.Transaction(async () =>
        {
            await Db.Transaction(() => Task.CompletedTask, cancellationToken: TestContext.Current.CancellationToken);
            await Db.Transaction(() => Task.CompletedTask, IsolationLevel.Serializable, TestContext.Current.CancellationToken);
        }, IsolationLevel.Serializable, TestContext.Current.CancellationToken);

        Assert.Equal([IsolationLevel.Serializable], _isolation.Started);
    }

    [Fact]
    public async Task No_isolation_asked_sends_none_and_the_database_default_applies()
    {
        await using var database = await StartDatabaseAsync();

        await Db.Transaction(() => Task.CompletedTask, cancellationToken: TestContext.Current.CancellationToken);
        await Db.Transaction(() => Task.CompletedTask, IsolationLevel.Serializable, TestContext.Current.CancellationToken);

        Assert.Equal([IsolationLevel.Unspecified, IsolationLevel.Serializable], _isolation.Started);
    }

    [Fact]
    public async Task A_db_context_handed_in_on_the_same_database_joins_and_rolls_back_with_the_work()
    {
        await using var database = await StartDatabaseAsync();
        await using var mine = Db.CreateOwnContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("staged"), db: mine, cancellationToken: TestContext.Current.CancellationToken);
            await mine.SaveChangesAsync(TestContext.Current.CancellationToken);
            throw new InvalidOperationException("roll back");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(0, await Widget.Count(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_db_context_on_another_database_is_refused_rather_than_run_outside()
    {
        await using var database = await StartDatabaseAsync();
        var otherPath = Path.Combine(Path.GetTempPath(), $"rask-transaction-other-{Guid.NewGuid():N}.db");
        await using var other = new RaskDbContext(new DbContextOptionsBuilder<RaskDbContext>().UseSqlite($"Data Source={otherPath}").Options);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(
            () => GeneratedModelWrites.Create(Widget.Create("elsewhere"), db: other, cancellationToken: TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("another database", refused.Message, StringComparison.Ordinal);
        File.Delete(otherPath);
    }

    [Fact]
    public async Task A_context_built_by_hand_and_never_handed_in_stays_outside()
    {
        await using var database = await StartDatabaseAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await using var mine = database.OpenRead();
            Assert.Null(mine.Database.CurrentTransaction);
            throw new InvalidOperationException("roll back");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(0, await Widget.Count(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task After_the_work_a_read_opens_its_own_context_again()
    {
        await using var database = await StartDatabaseAsync();
        Task<int>? late = null;
        var release = new TaskCompletionSource();

        await Db.Transaction(() =>
        {
            // Started inside, finished after: the transaction it captured is over by then.
            late = Task.Run(async () =>
            {
                await release.Task;
                return await Widget.Count(TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken);
            return GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);
        release.SetResult();

        Assert.Equal(1, await late!);
    }

    [Fact]
    public async Task On_SQLite_two_transactions_that_check_then_write_run_one_after_the_other()
    {
        await using var database = await StartDatabaseAsync();
        var firstChecked = new TaskCompletionSource();

        async Task TakeTheLastOne(bool first)
        {
            await Db.Transaction(async () =>
            {
                var taken = await Widget.Count(w => w.Name == "pistol-7", TestContext.Current.CancellationToken);
                if (first)
                {
                    firstChecked.SetResult();

                    // Let the second one try while this holds the write lock.
                    await Task.Delay(200, TestContext.Current.CancellationToken);
                }

                if (taken == 0)
                {
                    await GeneratedModelWrites.Create(Widget.Create("pistol-7"), cancellationToken: TestContext.Current.CancellationToken);
                }
            }, cancellationToken: TestContext.Current.CancellationToken);
        }

        var one = TakeTheLastOne(first: true);
        await firstChecked.Task;
        var two = Task.Run(() => TakeTheLastOne(first: false), TestContext.Current.CancellationToken);
        await Task.WhenAll(one, two);

        // SQLite has one writer: the second BEGIN waits for the first COMMIT, then counts the row it wrote.
        Assert.Equal(1, await Widget.Count(w => w.Name == "pistol-7", TestContext.Current.CancellationToken));
    }

    private static Task Queue(List<string> undone, string what)
    {
        AmbientTransaction.Current!.OnRollback(() =>
        {
            undone.Add(what);
            return Task.CompletedTask;
        });
        return Task.CompletedTask;
    }

    private async Task<TestDatabase> StartDatabaseAsync()
    {
        var database = await TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}").AddInterceptors(_isolation));

        // Creating the schema began one of its own.
        _isolation.Started.Clear();
        return database;
    }

    private sealed class IsolationRecorder : DbTransactionInterceptor
    {
        public List<IsolationLevel> Started { get; } = [];

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            Started.Add(eventData.IsolationLevel);
            return base.TransactionStartingAsync(connection, eventData, result, cancellationToken);
        }
    }
}
