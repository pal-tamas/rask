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

        var all = await Widget.All.ToListAsync();
        var ordered = await Widget.OrderBy(w => w.Name).ToListAsync();
        var filtered = await Widget.Where(w => w.Name != "beta").OrderByDescending(w => w.Name).ToListAsync();
        var page = await Widget.OrderBy(w => w.Name).Skip(1).Take(1).ToListAsync();
        var names = await Widget.OrderBy(w => w.Name).Select(w => w.Name).ToListAsync();

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

        Assert.Equal(2, await Widget.CountAsync());
        Assert.Equal(1, await Widget.CountAsync(w => w.Name == "alpha"));
        Assert.Equal(2L, await Widget.LongCountAsync());
        Assert.True(await Widget.AnyAsync());
        Assert.True(await Widget.AnyAsync(w => w.Name == "beta"));
        Assert.False(await Widget.AnyAsync(w => w.Name == "nope"));
        Assert.Equal(2, (await Widget.ToArrayAsync()).Length);
        Assert.NotNull(await Widget.SingleOrDefaultAsync(w => w.Name == "alpha"));
        Assert.Null(await Widget.FirstOrDefaultAsync(w => w.Name == "nope"));
    }

    [Fact]
    public async Task A_query_held_between_runs_sees_the_rows_written_since()
    {
        // Every terminal opens its own context, so a half-built query kept in a field is not pinned to
        // what the database held when it was built.
        var live = Widget.Where(w => w.Name != "hidden");
        Assert.Equal(0, await live.CountAsync());

        await SeedAsync("alpha", "hidden");

        Assert.Equal(1, await live.CountAsync());
        Assert.Equal(["alpha"], (await live.OrderBy(w => w.Name).ToListAsync()).Select(w => w.Name));
    }

    [Fact]
    public void ThenBy_before_any_ordering_says_what_to_call_first()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Widget.All.ThenBy(w => w.Name));

        Assert.Contains("OrderBy", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_generated_delete_soft_deletes_through_the_registered_interceptor()
    {
        var doomed = (await SeedAsync("doomed"))[0];

        await GeneratedModelWrites.DeleteAsync<Widget>(doomed.Id, version: null);

        // Gone from ordinary queries — the global filter ApplyRaskConventions added.
        Assert.Equal(0, await Widget.CountAsync());

        // Still there, stamped, behind IgnoreQueryFilters.
        var deleted = await Widget.IgnoreQueryFilters().ToListAsync();
        Assert.Single(deleted);
        Assert.NotNull(deleted[0].DeletedAt);
    }

    [Fact]
    public async Task A_generated_create_publishes_its_domain_events_after_commit()
    {
        var widget = await GeneratedModelWrites.CreateAsync(Widget.Create("evented"));

        Assert.Contains(_recorder.Events, e => e is WidgetCreated created && created.Id == widget.Id);
        Assert.Empty(widget.DomainEvents);
    }

    [Fact]
    public async Task A_generated_update_publishes_the_events_its_change_raised()
    {
        var widget = (await SeedAsync("before"))[0];

        await GeneratedModelWrites.UpdateAsync<Widget>(widget.Id, version: null, w => w.Rename("after"));

        Assert.Contains(_recorder.Events, e => e is WidgetRenamed renamed && renamed.Id == widget.Id);
        Assert.Equal(1, await Widget.CountAsync(w => w.Name == "after"));
    }

    [Fact]
    public async Task QueryAsync_is_a_real_escape_hatch_to_the_live_queryable()
    {
        await SeedAsync("alpha", "beta", "gamma");

        var initials = await Widget.QueryAsync((q, ct) => q
            .GroupBy(w => w.Name.Substring(0, 1))
            .Select(g => new { Initial = g.Key, Count = g.Count() })
            .OrderBy(x => x.Initial)
            .ToListAsync(ct));

        Assert.Equal(["a", "b", "g"], initials.Select(x => x.Initial));
        Assert.All(initials, x => Assert.Equal(1, x.Count));
    }

    [Fact]
    public async Task A_read_before_Configure_says_how_to_configure()
    {
        Db.Reset();

        Assert.False(Db.IsConfigured);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Widget.CountAsync());
        Assert.Contains("Db.Configure", error.Message, StringComparison.Ordinal);

        // A queryable composed before startup (a page field, say) fails the same way when it runs, not
        // when it is built.
        var query = Widget.AsQueryable().Where(w => w.Name != "");
        Assert.Throws<InvalidOperationException>(() => query.Count());
    }

    private async Task<Widget[]> SeedAsync(params string[] names)
    {
        var widgets = names.Select(Widget.Create).ToArray();

        await using var db = _provider.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContext();
        db.Widgets.AddRange(widgets);
        await db.SaveChangesAsync();

        return widgets;
    }
}
