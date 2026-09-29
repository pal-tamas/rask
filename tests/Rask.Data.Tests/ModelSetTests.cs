using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

// The read surface on the entity, end to end against a real SQLite file and bound the way an application
// binds it — AddRaskData<TContext>() and one Db.Configure(services). No DbContext is named below the
// fixture except to seed rows, which is the whole point of it.
//
// In the data-db collection because Db's configuration is process-wide — two classes configuring it at
// once would point one another's queries at the wrong file.
[Collection(DataDbCollection.Name)]
public sealed class ModelSetTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-entityset-{Guid.NewGuid():N}.db");
    private readonly EventRecorder _recorder = new();
    private readonly ServiceProvider _provider;

    public ModelSetTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_recorder);
        services.AddRaskCqrs();
        services.AddRaskData<TestDbContext>();
        services.AddDbContextFactory<TestDbContext>((sp, o) => o
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        // The read faces are queried through a context of their own, over the same file. A Rask host
        // registers this beside the app's own context; an app wiring EF itself says it here.
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));

        // The read faces are queried through a context of their own, over the same file. A Rask host
        // registers this beside the app's own context; an app wiring EF itself says it here.
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));

        _provider = services.BuildServiceProvider();

        using (var db = _provider.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContext())
        {
            db.Database.EnsureCreated();
        }

        Db.Configure(_provider);
    }

    public void Dispose()
    {
        Db.Reset();
        _provider.Dispose();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task Queries_compose_with_no_context_in_scope()
    {
        await SeedAsync("alpha", "beta", "gamma");

        var all = await Widget.All;
        var ordered = await Widget.OrderBy(w => w.Name);
        var filtered = await Widget.Where(w => w.Name != "beta").OrderByDescending(w => w.Name);
        var page = await Widget.OrderBy(w => w.Name).Skip(1).Take(1);
        var names = await Widget.OrderBy(w => w.Name).Select(w => w.Name);

        Assert.Equal(3, all.Count);
        Assert.Equal(["alpha", "beta", "gamma"], ordered.Select(w => w.Name));
        Assert.Equal(["gamma", "alpha"], filtered.Select(w => w.Name));
        Assert.Equal(["beta"], page.Select(w => w.Name));
        Assert.Equal(["alpha", "beta", "gamma"], names);
    }

    [Fact]
    public async Task Terminal_reads_cover_the_aggregate_shapes()
    {
        await SeedAsync("alpha", "beta");

        Assert.Equal(2, await Widget.Count(TestContext.Current.CancellationToken));
        Assert.Equal(1, await Widget.Count(w => w.Name == "alpha", TestContext.Current.CancellationToken));
        Assert.Equal(2L, await Widget.LongCount(TestContext.Current.CancellationToken));
        Assert.True(await Widget.Any(TestContext.Current.CancellationToken));
        Assert.True(await Widget.Any(w => w.Name == "beta", TestContext.Current.CancellationToken));
        Assert.False(await Widget.Any(w => w.Name == "nope", TestContext.Current.CancellationToken));
        Assert.Equal(2, (await Widget.All).Count);
        Assert.NotNull(await Widget.Single(w => w.Name == "alpha", TestContext.Current.CancellationToken));
        Assert.Null(await Widget.First(w => w.Name == "nope", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_read_given_no_token_is_cancelled_with_the_work_it_belongs_to()
    {
        await SeedAsync("alpha");
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();

        using var work = Ambient.Enter(stopped.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Widget.All);
#pragma warning disable xUnit1051 // the missing token is the point: the read must pick up the ambient one
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Widget.Count());
#pragma warning restore xUnit1051
    }

    [Fact]
    public async Task A_query_held_between_runs_sees_the_rows_written_since()
    {
        // Every terminal opens its own context, so a half-built query kept in a field is not pinned to
        // what the database held when it was built.
        var live = Widget.Where(w => w.Name != "hidden");
        Assert.Equal(0, await live.Count(TestContext.Current.CancellationToken));

        await SeedAsync("alpha", "hidden");

        Assert.Equal(1, await live.Count(TestContext.Current.CancellationToken));
        Assert.Equal(["alpha"], (await live.OrderBy(w => w.Name)).Select(w => w.Name));
    }

    [Fact]
    public void ThenBy_before_any_ordering_says_what_to_call_first()
    {
        // Widget.ThenBy does not compile at all; a filtered query is still unordered.
        var error = Assert.Throws<InvalidOperationException>(() => Widget.Where(w => w.Name != "").ThenBy(w => w.Name));

        Assert.Contains("OrderBy", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_delete_through_a_context_soft_deletes_and_the_read_surface_hides_it()
    {
        var doomed = (await SeedAsync("doomed"))[0];

        await using (var db = NewContext())
        {
            db.Remove((await db.Widgets.FindAsync([doomed.Id], TestContext.Current.CancellationToken))!);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Gone from ordinary queries — the global filter ApplyRaskConventions added.
        Assert.Equal(0, await Widget.Count(TestContext.Current.CancellationToken));

        // Still there, stamped, behind IgnoreQueryFilters.
        var deleted = await Widget.IgnoreQueryFilters();
        Assert.Single(deleted);
        Assert.NotNull(deleted[0].DeletedAt);
    }

    [Fact]
    public async Task A_create_publishes_its_domain_events_after_commit()
    {
        var widget = (await SeedAsync("evented"))[0];

        Assert.Contains(_recorder.Events, e => e is WidgetCreated created && created.Id == widget.Id);
        Assert.Empty(widget.DomainEvents);
    }

    [Fact]
    public async Task Saving_a_found_aggregate_publishes_the_events_its_methods_raised()
    {
        var widget = (await SeedAsync("before"))[0];

        var found = await Widget.Find(widget.Id, cancellationToken: TestContext.Current.CancellationToken);
        found!.Rename("after");
        await found.Save(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(_recorder.Events, e => e is WidgetRenamed renamed && renamed.Id == widget.Id);
        Assert.Empty(found.DomainEvents);
    }

    [Fact]
    public async Task An_update_publishes_the_events_its_change_raised()
    {
        var widget = (await SeedAsync("before"))[0];

        await using (var db = NewContext())
        {
            (await db.Widgets.FindAsync([widget.Id], TestContext.Current.CancellationToken))!.Rename("after");
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Contains(_recorder.Events, e => e is WidgetRenamed renamed && renamed.Id == widget.Id);
        Assert.Equal(1, await Widget.Count(w => w.Name == "after", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Query_is_a_real_escape_hatch_to_the_live_queryable()
    {
        await SeedAsync("alpha", "beta", "gamma");

        var initials = await Widget.Query((q, ct) => q
            .GroupBy(w => w.Name.Substring(0, 1))
            .Select(g => new { Initial = g.Key, Count = g.Count() })
            .OrderBy(x => x.Initial)
            .ToListAsync(ct), TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b", "g"], initials.Select(x => x.Initial));
        Assert.All(initials, x => Assert.Equal(1, x.Count));
    }

    [Fact]
    public async Task A_read_before_Configure_says_how_to_configure()
    {
        Db.Reset();

        Assert.False(Db.IsConfigured);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Widget.Count(TestContext.Current.CancellationToken));
        Assert.Contains("Db.Configure", error.Message, StringComparison.Ordinal);

        // A queryable composed before startup (a page field, say) fails the same way when it runs, not
        // when it is built.
        var query = Widget.AsQueryable().Where(w => w.Name != "");
        Assert.Throws<InvalidOperationException>(() => query.Count());
    }

    private TestDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContext();

    private async Task<Widget[]> SeedAsync(params string[] names)
    {
        var widgets = names.Select(Widget.Create).ToArray();

        await using var db = NewContext();
        db.Widgets.AddRange(widgets);
        await db.SaveChangesAsync();

        return widgets;
    }
}
