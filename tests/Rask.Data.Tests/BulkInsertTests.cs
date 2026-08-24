using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

// BulkInsertAsync against a real SQLite file. The load is batched and the change tracker is cleared between
// batches, so the questions worth pinning are: does everything land, do the interceptors still run over
// entities that only exist in the tracker for one batch, where does the transaction boundary sit, and does
// the domain-event guard hold — inside a transaction the interceptor would publish before the commit.
[Collection(DataDbCollection.Name)]
public sealed class BulkInsertTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-bulk-test-{Guid.NewGuid():N}.db");
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
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    private TestDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContext();

    private static Widget[] Widgets(int count) =>
        [.. Enumerable.Range(0, count).Select(i => Widget.Create($"widget-{i}"))];

    // Inside a transaction the load rejects entities carrying domain events, so a transactional test uses
    // rows whose creation event has already been dealt with.
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
            var written = await db.BulkInsertAsync(Widgets(25), o => o.BatchSize = 4);
            Assert.Equal(25, written);
        }

        await using (var db = NewContext())
        {
            Assert.Equal(25, await db.Widgets.CountAsync());
        }
    }

    [Fact]
    public async Task Auditing_interceptor_still_stamps_every_batched_row()
    {
        await using (var db = NewContext())
        {
            await db.BulkInsertAsync(Widgets(5), o => o.BatchSize = 2);
        }

        await using (var verify = NewContext())
        {
            var stamped = await verify.Widgets.ToListAsync();
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
            await db.BulkInsertAsync(Widgets(5), o => o.BatchSize = 2);
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
            await db.SaveChangesAsync();
        }

        var fresh = Widget.Create("would-be-inserted");
        fresh.ClearDomainEvents();
        existing.ClearDomainEvents();

        await using (var db = NewContext())
        {
            // Batch 1 (fresh) commits to the transaction, batch 2 collides with `existing`'s primary key.
            await Assert.ThrowsAnyAsync<DbUpdateException>(
                () => db.BulkInsertAsync(
                    [fresh, existing],
                    o => { o.BatchSize = 1; o.SingleTransaction = true; }));
        }

        await using (var verify = NewContext())
        {
            // Only the pre-existing row survives: batch 1 went back with batch 2.
            Assert.Equal(1, await verify.Widgets.CountAsync());
            Assert.False(await verify.Widgets.AnyAsync(w => w.Id == fresh.Id));
        }
    }

    [Fact]
    public async Task An_ambient_transaction_still_owns_the_commit()
    {
        await using (var db = NewContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.BulkInsertAsync(QuietWidgets(6), o => o.BatchSize = 2);
            await transaction.RollbackAsync();
        }

        await using (var verify = NewContext())
        {
            Assert.Equal(0, await verify.Widgets.CountAsync());
        }
    }

    [Fact]
    public async Task Without_a_single_transaction_the_batches_that_committed_stay()
    {
        var existing = Widget.Create("already-there");
        await using (var db = NewContext())
        {
            db.Widgets.Add(existing);
            await db.SaveChangesAsync();
        }

        var fresh = Widget.Create("stays-put");

        await using (var db = NewContext())
        {
            // The default is one transaction per batch, so batch 1 is already committed when batch 2 fails.
            await Assert.ThrowsAnyAsync<DbUpdateException>(
                () => db.BulkInsertAsync([fresh, existing], o => o.BatchSize = 1));
        }

        await using (var verify = NewContext())
        {
            Assert.Equal(2, await verify.Widgets.CountAsync());
            Assert.True(await verify.Widgets.AnyAsync(w => w.Id == fresh.Id));
        }
    }

    [Fact]
    public async Task Refuses_entities_carrying_domain_events_inside_a_single_transaction()
    {
        await using var db = NewContext();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.BulkInsertAsync(Widgets(2), o => o.SingleTransaction = true));

        Assert.Contains("domain events", error.Message, StringComparison.Ordinal);
        Assert.Empty(_recorder.Events);
    }

    [Fact]
    public async Task Refuses_entities_carrying_domain_events_inside_an_ambient_transaction()
    {
        await using var db = NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.BulkInsertAsync(Widgets(2)));
    }

    [Fact]
    public async Task Allows_event_free_entities_inside_a_single_transaction()
    {
        await using (var db = NewContext())
        {
            Assert.Equal(6, await db.BulkInsertAsync(
                QuietWidgets(6),
                o => { o.BatchSize = 2; o.SingleTransaction = true; }));
        }

        await using (var verify = NewContext())
        {
            Assert.Equal(6, await verify.Widgets.CountAsync());
        }
    }

    [Fact]
    public async Task Refuses_a_context_holding_unsaved_changes()
    {
        await using var db = NewContext();
        db.Widgets.Add(Widget.Create("unsaved"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.BulkInsertAsync(Widgets(2)));

        Assert.Contains("no pending changes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_sequence_writes_nothing()
    {
        await using var db = NewContext();
        Assert.Equal(0, await db.BulkInsertAsync(Array.Empty<Widget>()));
    }

    [Fact]
    public async Task The_set_overload_inserts_through_its_own_context()
    {
        await using (var db = NewContext())
        {
            Assert.Equal(3, await db.Widgets.BulkInsertAsync(Widgets(3)));
        }

        await using (var verify = NewContext())
        {
            Assert.Equal(3, await verify.Widgets.CountAsync());
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
            () => db.BulkInsertAsync(Widgets(1), o => o.BatchSize = batchSize));
    }

    [Fact]
    public async Task Leaves_the_change_tracker_empty_and_change_detection_as_it_found_it()
    {
        await using var db = NewContext();
        Assert.True(db.ChangeTracker.AutoDetectChangesEnabled);

        await db.BulkInsertAsync(Widgets(4), o => o.BatchSize = 2);

        Assert.True(db.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    public void Dispose()
    {
        _provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-shm", $"{_dbPath}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
