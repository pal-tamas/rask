using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

// SkipChangeTracking writes rows with a prepared INSERT and never materialises an entity entry, so it has to
// reproduce by hand what the change tracker did for free. These pin the parts that could silently diverge:
// the values that reach the columns, the audit stamps AuditingInterceptor is no longer there to write, the
// transaction boundary, and every case the writer must refuse rather than write wrong rows.
[Collection(DataDbCollection.Name)]
public sealed class BulkInsertFastPathTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-bulk-fast-{Guid.NewGuid():N}.db");
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));
    private readonly EventRecorder _recorder = new();
    private readonly ServiceProvider _provider;

    public BulkInsertFastPathTests()
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

    // The writer refuses entities carrying domain events, so every fixture here clears them first.
    private static Widget[] Widgets(int count)
    {
        var widgets = Enumerable.Range(0, count).Select(i => Widget.Create($"widget-{i}")).ToArray();
        foreach (var widget in widgets)
        {
            widget.ClearDomainEvents();
        }

        return widgets;
    }

    [Fact]
    public async Task Writes_every_row_with_its_values_intact()
    {
        var widgets = Widgets(7);

        await using (var db = NewContext())
        {
            Assert.Equal(7, await db.BulkInsertAsync(widgets, o => o.SkipChangeTracking = true));
        }

        await using (var verify = NewContext())
        {
            var stored = await verify.Widgets.OrderBy(w => w.Name).ToListAsync();
            Assert.Equal(7, stored.Count);

            // Guid keys and string values must survive the value converters the writer applies by hand.
            Assert.Equal(widgets.Select(w => w.Id).OrderBy(id => id), stored.Select(w => w.Id).OrderBy(id => id));
            Assert.Equal(widgets.OrderBy(w => w.Name).Select(w => w.Name), stored.Select(w => w.Name));
            Assert.All(stored, w => Assert.Null(w.DeletedAt));
        }
    }

    [Fact]
    public async Task Stamps_the_audit_columns_from_the_registered_clock()
    {
        var widgets = Widgets(3);

        await using (var db = NewContext())
        {
            await db.BulkInsertAsync(widgets, o => o.SkipChangeTracking = true);
        }

        var expected = _clock.UtcNow.UtcDateTime;

        // Stamped in the row...
        await using (var verify = NewContext())
        {
            var stored = await verify.Widgets.ToListAsync();
            Assert.All(stored, w => Assert.Equal(expected, w.CreatedAt));
            Assert.All(stored, w => Assert.Equal(expected, w.UpdatedAt));
        }

        // ...and on the entity the caller still holds, as the interceptor would have done.
        Assert.All(widgets, w => Assert.Equal(expected, w.CreatedAt));
        Assert.All(widgets, w => Assert.Equal(expected, w.UpdatedAt));
    }

    [Fact]
    public async Task Writes_the_same_rows_the_tracked_path_writes()
    {
        var tracked = Widgets(4);
        var raw = Widgets(4);

        await using (var db = NewContext())
        {
            await db.BulkInsertAsync(tracked);
        }

        await using (var db = NewContext())
        {
            await db.BulkInsertAsync(raw, o => o.SkipChangeTracking = true);
        }

        await using (var verify = NewContext())
        {
            var stored = await verify.Widgets.ToListAsync();
            var byPath = stored.ToLookup(w => tracked.Any(t => t.Id == w.Id));

            Assert.Equal(4, byPath[true].Count());
            Assert.Equal(4, byPath[false].Count());

            // Same stamps, same version, same soft-delete state — the two paths agree column for column.
            Assert.Equal(
                byPath[true].Select(w => (w.CreatedAt, w.UpdatedAt, w.Version, w.DeletedAt)).Distinct(),
                byPath[false].Select(w => (w.CreatedAt, w.UpdatedAt, w.Version, w.DeletedAt)).Distinct());
        }
    }

    [Fact]
    public async Task Leaves_the_change_tracker_untouched()
    {
        await using var db = NewContext();

        await db.BulkInsertAsync(Widgets(5), o => { o.SkipChangeTracking = true; o.BatchSize = 2; });

        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Runs_no_save_changes_interceptor_at_all()
    {
        // The defining property of this path, and the reason it is opt-in: SaveChanges never happens, so no
        // interceptor — Rask's or your own — is given a chance. Asserting it here is also what stops the rest
        // of this class from passing on a silent fall-back to the tracked path.
        var counter = new CountingInterceptor();
        await using var db = new CountingContext($"Data Source={_dbPath}-count", counter);
        await db.Database.EnsureCreatedAsync();

        await db.BulkInsertAsync([new Note { Id = Guid.NewGuid(), Text = "raw" }], o => o.SkipChangeTracking = true);
        Assert.Equal(0, counter.Saves);

        await db.BulkInsertAsync([new Note { Id = Guid.NewGuid(), Text = "tracked" }]);
        Assert.Equal(1, counter.Saves);
    }

    [Fact]
    public async Task Refuses_entities_carrying_domain_events_and_says_why()
    {
        await using var db = NewContext();

        // Not cleared: Widget.Create raises WidgetCreated, which no interceptor would ever see here.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.BulkInsertAsync(
                [Widget.Create("noisy")],
                o => o.SkipChangeTracking = true));

        Assert.Contains("never be delivered", error.Message, StringComparison.Ordinal);
        Assert.Empty(_recorder.Events);
    }

    [Fact]
    public async Task Under_a_single_transaction_a_failure_rolls_the_whole_load_back()
    {
        var existing = Widgets(1)[0];
        await using (var db = NewContext())
        {
            db.Widgets.Add(existing);
            await db.SaveChangesAsync();
        }

        var fresh = Widgets(1)[0];

        await using (var db = NewContext())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => db.BulkInsertAsync(
                [fresh, existing],
                o => { o.SkipChangeTracking = true; o.SingleTransaction = true; o.BatchSize = 1; }));
        }

        await using (var verify = NewContext())
        {
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
            await db.BulkInsertAsync(Widgets(4), o => { o.SkipChangeTracking = true; o.BatchSize = 2; });
            await transaction.RollbackAsync();
        }

        await using (var verify = NewContext())
        {
            Assert.Equal(0, await verify.Widgets.CountAsync());
        }
    }

    [Fact]
    public async Task An_empty_sequence_writes_nothing()
    {
        await using var db = NewContext();
        Assert.Equal(0, await db.BulkInsertAsync(Array.Empty<Widget>(), o => o.SkipChangeTracking = true));
    }

    [Fact]
    public async Task Refuses_a_store_assigned_integer_key_and_names_it()
    {
        await using var db = new GeneratedKeyContext($"Data Source={_dbPath}-gen");
        await db.Database.EnsureCreatedAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.BulkInsertAsync([new Counter { Label = "a" }], o => o.SkipChangeTracking = true));

        Assert.Contains("store-assigned integer key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_an_entity_with_navigations_rather_than_dropping_related_rows()
    {
        await using var db = new GraphContext($"Data Source={_dbPath}-graph");
        await db.Database.EnsureCreatedAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.BulkInsertAsync(
                [new Parent { Id = Guid.NewGuid() }],
                o => o.SkipChangeTracking = true));

        Assert.Contains("navigations", error.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var stem in new[] { _dbPath, $"{_dbPath}-gen", $"{_dbPath}-graph", $"{_dbPath}-count" })
        {
            foreach (var path in new[] { stem, $"{stem}-shm", $"{stem}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}

// A store-assigned integer key: the writer must refuse it, because the value only exists after the insert.
public sealed class Counter
{
    public int Id { get; set; }

    public string Label { get; set; } = "";
}

public sealed class GeneratedKeyContext(string connectionString) : DbContext
{
    private readonly string _connectionString = connectionString;

    public DbSet<Counter> Counters => Set<Counter>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite(_connectionString);
}

// A navigation the writer would have to walk; refusing beats writing the parent and dropping the children.
public sealed class Parent
{
    public Guid Id { get; set; }

    public List<Child> Children { get; } = [];
}

public sealed class Child
{
    public Guid Id { get; set; }

    public Guid ParentId { get; set; }
}

public sealed class GraphContext(string connectionString) : DbContext
{
    private readonly string _connectionString = connectionString;

    public DbSet<Parent> Parents => Set<Parent>();

    public DbSet<Child> Children => Set<Child>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite(_connectionString);
}

// Counts the saves that reach the provider, so a test can prove the raw path never went through SaveChanges.
public sealed class CountingInterceptor : SaveChangesInterceptor
{
    public int Saves { get; private set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Saves++;
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

public sealed class Note
{
    public Guid Id { get; set; }

    public string Text { get; set; } = "";
}

public sealed class CountingContext(string connectionString, CountingInterceptor interceptor) : DbContext
{
    private readonly string _connectionString = connectionString;
    private readonly CountingInterceptor _interceptor = interceptor;

    public DbSet<Note> Notes => Set<Note>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite(_connectionString).AddInterceptors(_interceptor);
}
