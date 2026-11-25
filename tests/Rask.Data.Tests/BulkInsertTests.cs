using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

// BulkInsert against a real SQLite file. The load is batched and the change tracker is cleared between
// batches, so the questions worth pinning are: does everything land, do the interceptors still run over
// entities that only exist in the tracker for one batch, where does the transaction boundary sit, and do the
// domain events wait for the commit — once each, even when a retrying strategy runs the load again.
public sealed class BulkInsertTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly EventRecorder _recorder = new();
    private readonly ServiceProvider _provider;

    public BulkInsertTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton(_recorder);
        services.AddRaskCqrs();
        services.AddRaskData();
        services.AddDbContextFactory<TestDbContext>((sp, o) => o
            .UseSqlite(_sqlite.ConnectionString)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    private TestDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContext();

    private static Widget[] Widgets(int count) =>
        [.. Enumerable.Range(0, count).Select(i => Widget.Create($"widget-{i}"))];

    // Rows whose creation event has already been dealt with, for a test about rows rather than events.
    private static Widget[] QuietWidgets(int count)
    {
        var widgets = Widgets(count);
        foreach (var widget in widgets)
        {
            widget.ClearDomainEvents();
        }

        return widgets;
    }

    [Fact]
    public async Task Inserts_every_row_across_several_batches()
    {
        await using (var db = NewContext())
        {
            var written = await db.BulkInsert(Widgets(25), o => o.BatchSize = 4, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(25, written);
        }

        await using (var db = NewContext())
        {
            Assert.Equal(25, await db.Widgets.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Auditing_interceptor_still_stamps_every_batched_row()
    {
        await using (var db = NewContext())
        {
            await db.BulkInsert(Widgets(5), o => o.BatchSize = 2, cancellationToken: TestContext.Current.CancellationToken);
        }

        await using (var verify = NewContext())
        {
            var stamped = await verify.Widgets.ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(5, stamped.Count);
            Assert.All(stamped, w => Assert.Equal(_clock.UtcNow.UtcDateTime, w.CreatedAt));
            Assert.All(stamped, w => Assert.Equal(_clock.UtcNow.UtcDateTime, w.UpdatedAt));
        }
    }

    [Fact]
    public async Task Domain_events_are_published_for_every_batched_row()
    {
        await using (var db = NewContext())
        {
            await db.BulkInsert(Widgets(5), o => o.BatchSize = 2, cancellationToken: TestContext.Current.CancellationToken);
        }

        // Widget.Create raises WidgetCreated, so each row's event must survive its batch being cleared.
        Assert.Equal(5, _recorder.Events.OfType<WidgetCreated>().Count());
    }

    [Fact]
    public async Task A_failure_part_way_through_rolls_the_whole_load_back()
    {
        var existing = Widget.Create("already-there");
        await using (var db = NewContext())
        {
            db.Widgets.Add(existing);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var fresh = Widget.Create("would-be-inserted");
        fresh.ClearDomainEvents();
        existing.ClearDomainEvents();

        await using (var db = NewContext())
        {
            // Batch 1 (fresh) commits to the transaction, batch 2 collides with `existing`'s primary key.
            await Assert.ThrowsAnyAsync<DbUpdateException>(
                () => db.BulkInsert(
                    [fresh, existing],
                    o => { o.BatchSize = 1; o.SingleTransaction = true; }, cancellationToken: TestContext.Current.CancellationToken));
        }

        await using (var verify = NewContext())
        {
            // Only the pre-existing row survives: batch 1 went back with batch 2.
            Assert.Equal(1, await verify.Widgets.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.False(await verify.Widgets.AnyAsync(w => w.Id == fresh.Id, cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task An_ambient_transaction_still_owns_the_commit()
    {
        await using (var db = NewContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await db.BulkInsert(QuietWidgets(6), o => o.BatchSize = 2, cancellationToken: TestContext.Current.CancellationToken);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        await using (var verify = NewContext())
        {
            Assert.Equal(0, await verify.Widgets.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Without_a_single_transaction_the_batches_that_committed_stay()
    {
        var existing = Widget.Create("already-there");
        await using (var db = NewContext())
        {
            db.Widgets.Add(existing);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var fresh = Widget.Create("stays-put");

        await using (var db = NewContext())
        {
            // The default is one transaction per batch, so batch 1 is already committed when batch 2 fails.
            await Assert.ThrowsAnyAsync<DbUpdateException>(
                () => db.BulkInsert([fresh, existing], o => o.BatchSize = 1, cancellationToken: TestContext.Current.CancellationToken));
        }

        await using (var verify = NewContext())
        {
            Assert.Equal(2, await verify.Widgets.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.True(await verify.Widgets.AnyAsync(w => w.Id == fresh.Id, cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Publishes_each_event_once_after_a_single_transaction_commits()
    {
        await using var db = NewContext();
        var widgets = Widgets(6);

        await db.BulkInsert(widgets, o => { o.BatchSize = 2; o.SingleTransaction = true; }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(widgets.Select(w => (IEvent)new WidgetCreated(w.Id)).ToHashSet(), _recorder.Events.ToHashSet());
        Assert.Equal(6, _recorder.Events.Count);
    }

    [Fact]
    public async Task Inside_a_transaction_of_the_callers_the_events_wait_for_its_commit()
    {
        await using var db = NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await db.BulkInsert(Widgets(2), cancellationToken: TestContext.Current.CancellationToken);
        var beforeCommit = _recorder.Events.Count;
        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, beforeCommit);
        Assert.Equal(2, _recorder.Events.Count);
    }

    [Fact]
    public async Task A_rolled_back_transaction_announces_nothing_and_gives_the_events_back()
    {
        await using var db = NewContext();
        var widgets = Widgets(2);
        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await db.BulkInsert(widgets, cancellationToken: TestContext.Current.CancellationToken);
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_recorder.Events);
        Assert.All(widgets, w => Assert.IsType<WidgetCreated>(Assert.Single(w.DomainEvents)));
    }

    [Fact]
    public async Task A_retried_single_transaction_inserts_and_announces_each_row_once()
    {
        var failOnce = new FailTheSecondBatchOnce();
        var services = new ServiceCollection();
        services.AddSingleton(_recorder);
        services.AddRaskCqrs();
        services.AddRaskData();
        services.AddDbContextFactory<TestDbContext>((sp, o) => o
            .UseSqlite(_sqlite.ConnectionString, s => s.ExecutionStrategy(d => new RetryOnTimeout(d)))
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>())
            .AddInterceptors(failOnce));
        await using var provider = services.BuildServiceProvider();
        await using var db = await provider.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);
        var widgets = Widgets(6);

        await db.BulkInsert(widgets, o => { o.BatchSize = 2; o.SingleTransaction = true; }, cancellationToken: TestContext.Current.CancellationToken);

        await using var verify = NewContext();
        Assert.True(failOnce.Failed);
        Assert.Equal(6, await verify.Widgets.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(6, _recorder.Events.Count);
        Assert.Equal(widgets.Select(w => (IEvent)new WidgetCreated(w.Id)).ToHashSet(), _recorder.Events.ToHashSet());
    }

    // A transient failure after the second batch of the first attempt: the strategy runs the whole load again.
    private sealed class FailTheSecondBatchOnce : SaveChangesInterceptor
    {
        private int _saves;

        public bool Failed { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Failed && ++_saves == 2)
            {
                Failed = true;
                throw new TimeoutException("transient");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class RetryOnTimeout(Microsoft.EntityFrameworkCore.Storage.ExecutionStrategyDependencies dependencies)
        : Microsoft.EntityFrameworkCore.Storage.ExecutionStrategy(dependencies, maxRetryCount: 2, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TimeoutException;
    }

    [Fact]
    public async Task Allows_event_free_entities_inside_a_single_transaction()
    {
        await using (var db = NewContext())
        {
            Assert.Equal(6, await db.BulkInsert(
                QuietWidgets(6),
                o => { o.BatchSize = 2; o.SingleTransaction = true; }, cancellationToken: TestContext.Current.CancellationToken));
        }

        await using (var verify = NewContext())
        {
            Assert.Equal(6, await verify.Widgets.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Refuses_a_context_holding_unsaved_changes()
    {
        await using var db = NewContext();
        db.Widgets.Add(Widget.Create("unsaved"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.BulkInsert(Widgets(2), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("no pending changes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_sequence_writes_nothing()
    {
        await using var db = NewContext();

        Assert.Equal(0, await db.BulkInsert(Array.Empty<Widget>(), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_set_overload_inserts_through_its_own_context()
    {
        await using (var db = NewContext())
        {
            Assert.Equal(3, await db.Widgets.BulkInsert(Widgets(3), cancellationToken: TestContext.Current.CancellationToken));
        }

        await using (var verify = NewContext())
        {
            Assert.Equal(3, await verify.Widgets.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_001)] // one past BulkInsertOptions.MaxBatchSize
    public async Task Rejects_a_batch_size_out_of_range(int batchSize)
    {
        await using var db = NewContext();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => db.BulkInsert(Widgets(1), o => o.BatchSize = batchSize, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Leaves_the_change_tracker_empty_and_change_detection_as_it_found_it()
    {
        await using var db = NewContext();
        Assert.True(db.ChangeTracker.AutoDetectChangesEnabled);

        await db.BulkInsert(Widgets(4), o => o.BatchSize = 2, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(db.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    public void Dispose()
    {
        _provider.Dispose();
        _sqlite.Dispose();
    }
}
