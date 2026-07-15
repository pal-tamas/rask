using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

// End-to-end against a real SQLite file: the interceptors + conventions drive auditing, transparent soft
// delete, optimistic concurrency, and after-commit domain-event publication.
public sealed class RaskDataTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-data-test-{Guid.NewGuid():N}.db");
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly EventRecorder _recorder = new();
    private readonly ServiceProvider _provider;

    public RaskDataTests()
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

    private TestDbContext NewContext() => _provider.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContext();

    [Fact]
    public async Task Auditing_stamps_created_on_insert_and_updated_on_change()
    {
        Guid id;
        await using (var db = NewContext())
        {
            var widget = Widget.Create("first");
            db.Widgets.Add(widget);
            await db.SaveChangesAsync();
            id = widget.Id;
            Assert.Equal(_clock.UtcNow.UtcDateTime, widget.CreatedAt);
            Assert.Equal(_clock.UtcNow.UtcDateTime, widget.UpdatedAt);
        }

        _clock.UtcNow = _clock.UtcNow.AddHours(3);

        await using (var db = NewContext())
        {
            var widget = await db.Widgets.SingleAsync(x => x.Id == id);
            widget.Rename("second");
            await db.SaveChangesAsync();
            Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcDateTime, widget.CreatedAt);
            Assert.Equal(_clock.UtcNow.UtcDateTime, widget.UpdatedAt);
        }
    }

    [Fact]
    public async Task Soft_delete_stamps_DeletedAt_and_the_query_filter_hides_the_row()
    {
        Guid id;
        await using (var db = NewContext())
        {
            var widget = Widget.Create("doomed");
            db.Widgets.Add(widget);
            await db.SaveChangesAsync();
            id = widget.Id;
        }

        await using (var db = NewContext())
        {
            var widget = await db.Widgets.SingleAsync(x => x.Id == id);
            db.Widgets.Remove(widget);
            await db.SaveChangesAsync();
        }

        await using (var read = NewContext())
        {
            Assert.Empty(await read.Widgets.ToListAsync());                       // hidden by the filter
            var raw = await read.Widgets.IgnoreQueryFilters().SingleAsync(x => x.Id == id);
            Assert.NotNull(raw.DeletedAt);                                        // still there, soft-deleted
        }
    }

    [Fact]
    public async Task Version_increments_on_update_and_a_stale_save_conflicts()
    {
        Guid id;
        await using (var db = NewContext())
        {
            var widget = Widget.Create("v");
            db.Widgets.Add(widget);
            await db.SaveChangesAsync();
            id = widget.Id;
            Assert.Equal(0, widget.Version);
        }

        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.Widgets.SingleAsync(x => x.Id == id);
        var b = await second.Widgets.SingleAsync(x => x.Id == id);

        a.Rename("a");
        await first.SaveChangesAsync();
        Assert.Equal(1, a.Version); // bumped by the interceptor

        b.Rename("b"); // b still holds Version 0
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Domain_events_are_published_after_commit_and_cleared()
    {
        Widget widget;
        await using (var db = NewContext())
        {
            widget = Widget.Create("evented");
            db.Widgets.Add(widget);
            await db.SaveChangesAsync();
        }

        Assert.Contains(_recorder.Events, e => e is WidgetCreated created && created.Id == widget.Id);
        Assert.Empty(widget.DomainEvents); // interceptor cleared them
    }

    [Fact]
    public async Task Domain_events_from_a_hard_delete_are_published()
    {
        Guid id;
        await using (var db = NewContext())
        {
            var widget = Widget.Create("to-delete");
            db.Widgets.Add(widget);
            await db.SaveChangesAsync();
            id = widget.Id;
        }

        await using (var db = NewContext())
        {
            var widget = await db.Widgets.SingleAsync(x => x.Id == id);
            widget.MarkDeleted();      // event raised before the row is physically removed
            db.Widgets.Remove(widget);
            await db.SaveChangesAsync();
        }

        // Collected in SavingChanges (while still tracked), so the delete doesn't lose the event.
        Assert.Contains(_recorder.Events, e => e is WidgetDeleted deleted && deleted.Id == id);
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
