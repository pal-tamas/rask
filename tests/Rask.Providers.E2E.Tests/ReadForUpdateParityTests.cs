using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>
///     A read face's <c>ForUpdate()</c> on every provider: the read is written in the database's own words
///     (<c>UPDLOCK, HOLDLOCK</c>, <c>FOR UPDATE OF</c>) and runs, and a second transaction reading the same month for update
///     waits for the first to commit — on SQLite because its one writer serializes the transactions, on a server
///     because of the lock.
/// </summary>
public abstract class ReadForUpdateParityTests(ProviderLeg leg) : ParityTests<LedgerDbContext>(leg)
{
    [Fact]
    public async Task A_month_read_for_update_inside_a_transaction_comes_back()
    {
        RequireServer();
        var month = await MonthAsync("June");

        var read = await Configured(() => Db.Transaction(
            async () => await LedgerMonth.Where(m => m.Id == month).ForUpdate(), cancellationToken: Ct));

        Assert.Equal("June", Assert.Single(read).Name);
    }

    [Fact]
    public async Task A_second_read_for_update_of_the_month_waits_for_the_first_to_commit()
    {
        RequireServer();
        var month = await MonthAsync("July");
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var waited = await Configured(async () =>
        {
            var first = Task.Run(() => Db.Transaction(async () =>
            {
                await LedgerMonth.Where(m => m.Id == month).ForUpdate();
                firstRead.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
            }, cancellationToken: Ct), Ct);
            await firstRead.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

            // On a thread of its own: SQLite's provider waits for its lock synchronously, inside the call.
            var second = Task.Run(() => Db.Transaction(
                async () => await LedgerMonth.Where(m => m.Id == month).ForUpdate(), cancellationToken: Ct), Ct);
            var held = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(500), Ct)) != second;
            release.TrySetResult();
            await first;
            await second;
            return held;
        });

        Assert.True(waited);
    }

    [Fact]
    public async Task A_set_read_for_update_waits_for_a_read_face_s_lock_of_the_same_month_to_commit()
    {
        RequireServer();
        var month = await MonthAsync("August");
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var (waited, read) = await Configured(async () =>
        {
            var first = Task.Run(() => Db.Transaction(async () =>
            {
                await LedgerMonth.Where(m => m.Id == month).ForUpdate();
                firstRead.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
            }, cancellationToken: Ct), Ct);
            await firstRead.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

            // Code generic over the type locks through the set, in the same words: it waits as a read face's lock would.
            var second = Task.Run(() => Db.Transaction(() => LockedAsync<LedgerMonth>(m => m.Id == month), cancellationToken: Ct), Ct);
            var held = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(500), Ct)) != second;
            release.TrySetResult();
            await first;
            return (held, await second);
        });

        Assert.True(waited);
        Assert.Equal("August", Assert.Single(read).Name);
    }

    [Fact]
    public async Task A_count_of_a_set_read_for_update_is_refused_on_every_provider()
    {
        RequireServer();
        var month = await MonthAsync("September");

        var refused = await Configured(() => Db.Transaction(async () =>
        {
            await using var db = Open();
            return await Record.ExceptionAsync(() => db.Set<LedgerMonth>().Where(m => m.Id == month).ForUpdate().CountAsync(Ct));
        }, cancellationToken: Ct));

        Assert.Contains("ForUpdate().Count()", Assert.IsType<InvalidOperationException>(refused).Message, StringComparison.Ordinal);
    }

    private protected override LedgerDbContext Open() => Leg.Open<LedgerDbContext>(o => new LedgerDbContext(o));

    // What an app's code generic over its types writes: the rows of a set named by its type, locked.
    private async Task<List<T>> LockedAsync<T>(System.Linq.Expressions.Expression<Func<T, bool>> which)
        where T : class
    {
        await using var db = Open();
        return await db.Set<T>().Where(which).ForUpdate().ToListAsync(Ct);
    }

    // Db and the read faces pointed at the leg for the length of the facts, as a host points them.
    private async Task<T> Configured<T>(Func<Task<T>> facts)
    {
        Db.Configure(Open);
        ReadDb.Configure(() => Leg.Open<RaskReadDbContext>(o => new RaskReadDbContext(o)));
        try
        {
            return await facts();
        }
        finally
        {
            ReadDb.Reset();
            Db.Reset();
        }
    }

    private async Task<int> MonthAsync(string name)
    {
        await using var db = Open();
        var month = LedgerMonth.Open(name);
        db.Add(month);
        await db.SaveChangesAsync(Ct);
        return month.Id;
    }
}

public sealed class SqliteReadForUpdateParityTests() : ReadForUpdateParityTests(ProviderLeg.Sqlite("read-for-update"));

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerReadForUpdateParityTests() : ReadForUpdateParityTests(ProviderLeg.SqlServer("rask_e2e_read_for_update"));

[Collection(PostgresCollection.Name)]
public sealed class PostgresReadForUpdateParityTests() : ReadForUpdateParityTests(ProviderLeg.Postgres(LedgerDbContext.Schema));
