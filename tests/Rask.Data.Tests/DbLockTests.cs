using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
///     <c>Db.Lock(name)</c> on SQLite: one holder of a name at a time, in this process — the host SQLite runs on.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class DbLockTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-lock-{Guid.NewGuid():N}.db");

    public async ValueTask InitializeAsync()
    {
        Db.Configure(() => new RaskDbContext(OptionsLayout.LikeAddDbContext<RaskDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(AmbientTransactionInterceptor.Instance)
            .Options));

        await using var schema = Db.CreateOwnContext();
        await schema.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Db.Reset();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_second_holder_of_the_same_name_waits_until_the_first_lets_go()
    {
        var first = await Db.Lock("pump-1", TestContext.Current.CancellationToken);

        var second = Db.Lock("pump-1", TestContext.Current.CancellationToken).AsTask();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        var waitedWhileHeld = !second.IsCompleted;
        await first.DisposeAsync();
        await using var taken = await second;

        Assert.True(waitedWhileHeld);
    }

    [Fact]
    public async Task A_name_held_elsewhere_times_out_with_the_name_and_the_wait()
    {
        await using var held = await Db.Lock("pump-1", TestContext.Current.CancellationToken);

        var refused = await Assert.ThrowsAsync<TimeoutException>(
            () => Db.Lock("pump-1", TestContext.Current.CancellationToken).For(TimeSpan.FromMilliseconds(50)).AsTask());

        Assert.Contains("pump-1", refused.Message, StringComparison.Ordinal);
        Assert.Contains("00:00:00.05", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_wait_on_a_held_name_times_out_at_once()
    {
        await using var held = await Db.Lock("pump-1", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<TimeoutException>(
            () => Db.Lock("pump-1", TestContext.Current.CancellationToken).For(TimeSpan.Zero).AsTask());
    }

    [Fact]
    public async Task Two_names_are_held_at_the_same_time()
    {
        await using var one = await Db.Lock("pump-1", TestContext.Current.CancellationToken);

        await using var other = await Db.Lock("pump-2", TestContext.Current.CancellationToken).For(TimeSpan.Zero);

        Assert.NotSame(one, other);
    }

    [Fact]
    public async Task A_cancelled_wait_ends_and_leaves_the_name_to_the_next_holder()
    {
        var held = await Db.Lock("pump-1", TestContext.Current.CancellationToken);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Db.Lock("pump-1", cancel.Token).AsTask());
        await held.DisposeAsync();

        await using var next = await Db.Lock("pump-1", TestContext.Current.CancellationToken).For(TimeSpan.Zero);
    }

    [Fact]
    public async Task A_lock_taken_inside_a_rolled_back_transaction_is_held_until_it_is_let_go()
    {
        IAsyncDisposable? inside = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            inside = await Db.Lock("pump-1", TestContext.Current.CancellationToken);
            throw new InvalidOperationException("rolled back");
        }, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<TimeoutException>(
            () => Db.Lock("pump-1", TestContext.Current.CancellationToken).For(TimeSpan.Zero).AsTask());
        await inside!.DisposeAsync();

        await using var after = await Db.Lock("pump-1", TestContext.Current.CancellationToken).For(TimeSpan.Zero);
    }

    [Fact]
    public async Task Letting_go_twice_lets_go_once()
    {
        var held = await Db.Lock("pump-1", TestContext.Current.CancellationToken);
        await held.DisposeAsync();
        var next = await Db.Lock("pump-1", TestContext.Current.CancellationToken).For(TimeSpan.Zero);

        await held.DisposeAsync();

        await Assert.ThrowsAsync<TimeoutException>(
            () => Db.Lock("pump-1", TestContext.Current.CancellationToken).For(TimeSpan.Zero).AsTask());
        await next.DisposeAsync();
    }

    [Fact]
    public async Task A_blank_name_is_refused()
    {
        var blank = "  ";

        var refused = Assert.Throws<ArgumentException>(() => Db.Lock(blank, TestContext.Current.CancellationToken));

        Assert.Equal("name", refused.ParamName);
    }

    [Fact]
    public void A_negative_wait_is_refused_where_it_is_written()
    {
        var locking = Db.Lock("pump-1", TestContext.Current.CancellationToken);

        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => locking.For(TimeSpan.FromSeconds(-1)));

        Assert.Equal("wait", refused.ParamName);
    }
}
