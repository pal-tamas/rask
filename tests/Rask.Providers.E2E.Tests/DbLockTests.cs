using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>
///     <c>Db.Lock(name)</c> the same on every database: one holder of a name at a time, a wait that runs out as a
///     <see cref="TimeoutException" />, and a lock that is its own connection's — not the <c>Db.Transaction</c>'s.
/// </summary>
/// <remarks>Each holder here is a connection of its own, so on a server it is the database that keeps them apart.</remarks>
internal static class LockRun
{
    internal static async Task Run(Action<DbContextOptionsBuilder> provider, Func<Task> facts)
    {
        Db.Configure(() =>
        {
            var builder = new DbContextOptionsBuilder<RaskDbContext>();
            provider(builder);
            return new RaskDbContext(builder.Options);
        });
        try
        {
            await facts();
        }
        finally
        {
            Db.Reset();
        }
    }

    internal static async Task A_second_holder_waits_until_the_first_lets_go()
    {
        var first = await Db.Lock("rask-lock-parity", TestContext.Current.CancellationToken);

        var second = Db.Lock("rask-lock-parity", TestContext.Current.CancellationToken).For(TimeSpan.FromSeconds(30)).AsTask();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        var waitedWhileHeld = !second.IsCompleted;
        await first.DisposeAsync();
        await using var taken = await second;

        Assert.True(waitedWhileHeld);
    }

    internal static async Task A_held_name_times_out_after_its_wait_and_with_none()
    {
        await using var held = await Db.Lock("rask-lock-parity", TestContext.Current.CancellationToken);

        var waited = await Assert.ThrowsAsync<TimeoutException>(
            () => Db.Lock("rask-lock-parity", TestContext.Current.CancellationToken).For(TimeSpan.FromMilliseconds(200)).AsTask());
        await Assert.ThrowsAsync<TimeoutException>(
            () => Db.Lock("rask-lock-parity", TestContext.Current.CancellationToken).For(TimeSpan.Zero).AsTask());

        Assert.Contains("rask-lock-parity", waited.Message, StringComparison.Ordinal);
    }

    internal static async Task A_cancelled_wait_ends_and_leaves_the_name_to_the_next_holder()
    {
        var held = await Db.Lock("rask-lock-parity", TestContext.Current.CancellationToken);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Db.Lock("rask-lock-parity", cancel.Token).AsTask());
        await held.DisposeAsync();

        await using var next = await Db.Lock("rask-lock-parity", TestContext.Current.CancellationToken).For(TimeSpan.Zero);
    }

    internal static async Task A_lock_taken_inside_a_rolled_back_transaction_is_held_until_it_is_let_go()
    {
        IAsyncDisposable? inside = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            inside = await Db.Lock("rask-lock-parity", TestContext.Current.CancellationToken);
            throw new InvalidOperationException("roll back");
        }, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<TimeoutException>(
            () => Db.Lock("rask-lock-parity", TestContext.Current.CancellationToken).For(TimeSpan.Zero).AsTask());
        await inside!.DisposeAsync();

        await using var after = await Db.Lock("rask-lock-parity", TestContext.Current.CancellationToken).For(TimeSpan.Zero);
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerDbLockTests
{
    [Fact]
    public async Task A_second_holder_waits_until_the_first_lets_go()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);

        var run = LockRun.Run(Server, LockRun.A_second_holder_waits_until_the_first_lets_go);

        await run;
    }

    [Fact]
    public async Task A_held_name_times_out_after_its_wait_and_with_none()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);

        var run = LockRun.Run(Server, LockRun.A_held_name_times_out_after_its_wait_and_with_none);

        await run;
    }

    [Fact]
    public async Task A_cancelled_wait_ends_and_leaves_the_name_to_the_next_holder()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);

        var run = LockRun.Run(Server, LockRun.A_cancelled_wait_ends_and_leaves_the_name_to_the_next_holder);

        await run;
    }

    [Fact]
    public async Task A_lock_taken_inside_a_rolled_back_transaction_is_held_until_it_is_let_go()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);

        var run = LockRun.Run(Server, LockRun.A_lock_taken_inside_a_rolled_back_transaction_is_held_until_it_is_let_go);

        await run;
    }

    [Fact]
    public async Task A_name_longer_than_sp_getapplock_takes_is_refused()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        var name = new string('n', 256);

        var refused = LockRun.Run(Server, () => Assert.ThrowsAsync<ArgumentException>(
            () => Db.Lock(name, TestContext.Current.CancellationToken).AsTask()));

        await refused;
    }

    private static void Server(DbContextOptionsBuilder o) => o.UseSqlServer(SqlServer.ConnectionString);
}

[Collection(PostgresCollection.Name)]
public sealed class PostgresDbLockTests
{
    [Fact]
    public async Task A_second_holder_waits_until_the_first_lets_go()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);

        var run = LockRun.Run(Server, LockRun.A_second_holder_waits_until_the_first_lets_go);

        await run;
    }

    [Fact]
    public async Task A_held_name_times_out_after_its_wait_and_with_none()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);

        var run = LockRun.Run(Server, LockRun.A_held_name_times_out_after_its_wait_and_with_none);

        await run;
    }

    [Fact]
    public async Task A_cancelled_wait_ends_and_leaves_the_name_to_the_next_holder()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);

        var run = LockRun.Run(Server, LockRun.A_cancelled_wait_ends_and_leaves_the_name_to_the_next_holder);

        await run;
    }

    [Fact]
    public async Task A_lock_taken_inside_a_rolled_back_transaction_is_held_until_it_is_let_go()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);

        var run = LockRun.Run(Server, LockRun.A_lock_taken_inside_a_rolled_back_transaction_is_held_until_it_is_let_go);

        await run;
    }

    [Fact]
    public async Task A_wait_that_runs_out_leaves_the_session_s_lock_timeout_as_it_was()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var context = new RaskDbContext(new DbContextOptionsBuilder<RaskDbContext>().UseNpgsql(Postgres.Required).Options);

        var timeouts = await LockRunTimeouts(context);

        Assert.Equal(["0", "0"], timeouts);
    }

    private static async Task<List<string>> LockRunTimeouts(RaskDbContext context)
    {
        var seen = new List<string>();
        await LockRun.Run(Server, async () =>
        {
            seen.Add(await Timeout(context));
            await using var held = await Db.Lock("rask-lock-parity", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<TimeoutException>(
                () => Db.Lock("rask-lock-parity", TestContext.Current.CancellationToken).For(TimeSpan.FromMilliseconds(100)).AsTask());
        });
        seen.Add(await Timeout(context));
        return seen;
    }

    // A fresh session reads the server's default; the one that timed out went back to the pool and is the next drawn.
    private static Task<string> Timeout(RaskDbContext context) =>
        context.Database.SqlQueryRaw<string>("SELECT current_setting('lock_timeout') AS \"Value\"")
            .SingleAsync(TestContext.Current.CancellationToken);

    private static void Server(DbContextOptionsBuilder o) => o.UseNpgsql(Postgres.Required);
}

/// <summary>SQLite's lock is the process's: alone in its collection, since <c>Db</c> is process-wide.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqliteDbLockTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rask-lock-parity-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    [Fact]
    public async Task A_second_holder_waits_until_the_first_lets_go()
    {
        var path = _path;

        var run = LockRun.Run(o => o.UseSqlite($"Data Source={path}"), LockRun.A_second_holder_waits_until_the_first_lets_go);

        await run;
    }

    [Fact]
    public async Task A_held_name_times_out_after_its_wait_and_with_none()
    {
        var path = _path;

        var run = LockRun.Run(o => o.UseSqlite($"Data Source={path}"), LockRun.A_held_name_times_out_after_its_wait_and_with_none);

        await run;
    }

    [Fact]
    public async Task A_cancelled_wait_ends_and_leaves_the_name_to_the_next_holder()
    {
        var path = _path;

        var run = LockRun.Run(o => o.UseSqlite($"Data Source={path}"), LockRun.A_cancelled_wait_ends_and_leaves_the_name_to_the_next_holder);

        await run;
    }

    [Fact]
    public async Task A_lock_taken_inside_a_rolled_back_transaction_is_held_until_it_is_let_go()
    {
        var path = _path;

        var run = LockRun.Run(
            o => o.UseSqlite($"Data Source={path}"), LockRun.A_lock_taken_inside_a_rolled_back_transaction_is_held_until_it_is_let_go);

        await run;
    }
}
