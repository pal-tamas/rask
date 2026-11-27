using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

// End-to-end against a real SQLite file: the interceptors + conventions drive auditing, transparent soft
// delete, optimistic concurrency, and after-commit domain-event publication.
public sealed class RaskDataTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
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
            .UseSqlite(_sqlite.ConnectionString)
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
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = widget.Id;
            Assert.Equal(_clock.UtcNow.UtcDateTime, widget.CreatedAt.Instant.UtcDateTime);
            Assert.Equal(_clock.UtcNow.UtcDateTime, widget.UpdatedAt.Instant.UtcDateTime);
        }

        _clock.UtcNow = _clock.UtcNow.AddHours(3);

        await using (var db = NewContext())
        {
            var widget = await db.Widgets.SingleAsync(x => x.Id == id, cancellationToken: TestContext.Current.CancellationToken);
            widget.Rename("second");
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcDateTime, widget.CreatedAt.Instant.UtcDateTime);
            Assert.Equal(_clock.UtcNow.UtcDateTime, widget.UpdatedAt.Instant.UtcDateTime);
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
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = widget.Id;
        }

        await using (var db = NewContext())
        {
            var widget = await db.Widgets.SingleAsync(x => x.Id == id, cancellationToken: TestContext.Current.CancellationToken);
            db.Widgets.Remove(widget);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var read = NewContext())
        {
            Assert.Empty(await read.Widgets.ToListAsync(cancellationToken: TestContext.Current.CancellationToken));                       // hidden by the filter
            var raw = await read.Widgets.IgnoreQueryFilters().SingleAsync(x => x.Id == id, cancellationToken: TestContext.Current.CancellationToken);
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
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = widget.Id;
            Assert.Equal(0, widget.Version);
        }

        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.Widgets.SingleAsync(x => x.Id == id, cancellationToken: TestContext.Current.CancellationToken);
        var b = await second.Widgets.SingleAsync(x => x.Id == id, cancellationToken: TestContext.Current.CancellationToken);

        a.Rename("a");
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, a.Version); // bumped by the interceptor

        b.Rename("b"); // b still holds Version 0
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void An_aggregate_that_raised_nothing_has_no_events_and_clears_without_one()
    {
        var memo = Memo.Write("quiet");

        memo.ClearDomainEvents();

        Assert.Empty(memo.DomainEvents);
    }

    [Fact]
    public async Task Domain_events_are_published_after_commit_and_cleared()
    {
        Widget widget;
        await using (var db = NewContext())
        {
            widget = Widget.Create("evented");
            db.Widgets.Add(widget);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Contains(_recorder.Events, e => e is WidgetCreated created && created.Id == widget.Id);
        Assert.Empty(widget.DomainEvents); // interceptor cleared them
    }

    [Fact]
    public async Task Under_the_events_fake_a_save_records_its_domain_events_and_runs_their_handlers_only_when_asked()
    {
        using var events = Events.Fake();
        var widget = Widget.Create("faked");
        await using (var db = NewContext())
        {
            db.Widgets.Add(widget);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var heardBeforeRun = _recorder.Events.Count;
        await events.Run(TestContext.Current.CancellationToken);

        events.Published<WidgetCreated>().Where(e => e.Id == widget.Id).Once();
        Assert.Equal(0, heardBeforeRun);
        Assert.Contains(_recorder.Events, e => e is WidgetCreated created && created.Id == widget.Id);
    }

    [Fact]
    public async Task A_save_inside_a_transaction_of_its_own_publishes_its_events_once_that_commits()
    {
        await using var db = NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var widget = Widget.Create("held");
        db.Widgets.Add(widget);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var beforeCommit = _recorder.Events.Count;
        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, beforeCommit);
        Assert.Contains(_recorder.Events, e => e is WidgetCreated created && created.Id == widget.Id);
    }

    [Fact]
    public async Task A_save_inside_a_transaction_that_rolls_back_publishes_nothing()
    {
        await using var db = NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        db.Widgets.Add(Widget.Create("undone"));

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_recorder.Events);
    }

    [Fact]
    public async Task A_save_retried_after_it_failed_publishes_its_events_once()
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER no_widgets BEFORE INSERT ON Widgets BEGIN SELECT RAISE(ABORT, 'busy'); END;",
            TestContext.Current.CancellationToken);
        var widget = Widget.Create("retried");
        db.Widgets.Add(widget);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        var afterFailure = _recorder.Events.Count;
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER no_widgets;", TestContext.Current.CancellationToken);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, afterFailure);
        Assert.Single(_recorder.Events, e => e is WidgetCreated created && created.Id == widget.Id);
    }

    [Fact]
    public async Task Domain_events_from_a_hard_delete_are_published()
    {
        Guid id;
        await using (var db = NewContext())
        {
            var widget = Widget.Create("to-delete");
            db.Widgets.Add(widget);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = widget.Id;
        }

        await using (var db = NewContext())
        {
            var widget = await db.Widgets.SingleAsync(x => x.Id == id, cancellationToken: TestContext.Current.CancellationToken);
            widget.MarkDeleted();      // event raised before the row is physically removed
            db.Widgets.Remove(widget);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Collected in SavingChanges (while still tracked), so the delete doesn't lose the event.
        Assert.Contains(_recorder.Events, e => e is WidgetDeleted deleted && deleted.Id == id);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _sqlite.Dispose();
    }
}
