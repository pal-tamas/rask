using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

// The DbSet-on-the-entity surface, end to end against a real SQLite file: no DbContext is injected or
// named anywhere below the fixture, which is the whole point of it.
//
// In the data-db collection because Db's ambient state is process-wide — two classes configuring it at
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
    public async Task Add_inside_a_unit_of_work_writes_on_save()
    {
        await using (var uow = Db.Begin())
        {
            Widget.Add(Widget.Create("anvil"));
            Assert.Equal(1, await uow.SaveChangesAsync());
        }

        Assert.Equal(1, await Widget.CountAsync());
        Assert.NotNull(await Widget.FirstOrDefaultAsync(w => w.Name == "anvil"));
    }

    [Fact]
    public async Task Add_without_a_unit_of_work_says_so_rather_than_doing_nothing()
    {
        // The failure mode this replaces is the silent one: EF's Add writes nothing until a save, so a
        // tracker verb with no unit of work behind it would look like it worked.
        var error = Assert.Throws<InvalidOperationException>(() => Widget.Add(Widget.Create("orphan")));

        Assert.Contains("Db.Begin()", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, await Widget.CountAsync());
    }

    [Fact]
    public async Task A_unit_of_work_disposed_without_saving_writes_nothing()
    {
        await using (var uow = Db.Begin())
        {
            Widget.Add(Widget.Create("discarded"));
            _ = uow;
        }

        Assert.Equal(0, await Widget.CountAsync());
    }

    [Fact]
    public async Task One_unit_of_work_commits_several_entities_together()
    {
        await using (var uow = Db.Begin())
        {
            Widget.AddRange(Widget.Create("a"), Widget.Create("b"), Widget.Create("c"));
            Assert.Equal(3, await uow.SaveChangesAsync());
        }

        Assert.Equal(3, await Widget.CountAsync());
    }

    [Fact]
    public async Task Queries_need_no_unit_of_work_and_leave_nothing_open()
    {
        await SeedAsync("alpha", "beta", "gamma");

        Assert.False(Db.HasCurrent);

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
        Assert.False(Db.HasCurrent);
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
    public async Task Queries_are_no_tracking_by_default_and_AsTracking_opts_in()
    {
        await SeedAsync("before");

        // Untracked: the mutation is invisible to the save.
        await using (var uow = Db.Begin())
        {
            var widget = await Widget.FirstOrDefaultAsync(w => w.Name == "before");
            widget!.Rename("ignored");
            Assert.Equal(0, await uow.SaveChangesAsync());
        }

        Assert.Equal("before", (await Widget.FirstOrDefaultAsync(w => w.Name == "before"))!.Name);

        // Tracked: the same code writes.
        await using (var uow = Db.Begin())
        {
            var widget = await Widget.AsTracking().Where(w => w.Name == "before").FirstOrDefaultAsync();
            widget!.Rename("after");
            Assert.Equal(1, await uow.SaveChangesAsync());
        }

        Assert.Equal(1, await Widget.CountAsync(w => w.Name == "after"));
    }

    [Fact]
    public async Task Update_writes_back_an_entity_that_was_read_untracked()
    {
        // The intended round trip under the no-tracking default: read, change, hand it back.
        await SeedAsync("before");

        var widget = await Widget.FirstOrDefaultAsync(w => w.Name == "before");
        widget!.Rename("after");

        await using (var uow = Db.Begin())
        {
            Widget.Update(widget);
            Assert.Equal(1, await uow.SaveChangesAsync());
        }

        Assert.Equal(1, await Widget.CountAsync(w => w.Name == "after"));
    }

    [Fact]
    public async Task Remove_soft_deletes_through_the_interceptor()
    {
        await SeedAsync("doomed");

        await using (var uow = Db.Begin())
        {
            var widget = await Widget.AsTracking().FirstOrDefaultAsync(w => w.Name == "doomed");
            Widget.Remove(widget!);
            await uow.SaveChangesAsync();
        }

        // Gone from ordinary queries — the global filter ApplyRaskConventions added.
        Assert.Equal(0, await Widget.CountAsync());

        // Still there, stamped, behind IgnoreQueryFilters.
        var deleted = await Widget.IgnoreQueryFilters().ToListAsync();
        Assert.Single(deleted);
        Assert.NotNull(deleted[0].DeletedAt);
    }

    [Fact]
    public async Task FindAsync_returns_the_row_by_key()
    {
        Guid id;
        await using (var uow = Db.Begin())
        {
            var widget = Widget.Create("findable");
            Widget.Add(widget);
            await uow.SaveChangesAsync();
            id = widget.Id;
        }

        var found = await Widget.FindAsync(id);

        Assert.NotNull(found);
        Assert.Equal("findable", found.Name);
        Assert.Null(await Widget.FindAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_nested_Begin_joins_the_outer_unit_of_work_and_does_not_commit_it()
    {
        await using (var outer = Db.Begin())
        {
            await using (var inner = Db.Begin())
            {
                Assert.False(inner.IsRoot);
                Assert.Same(outer.Context, inner.Context);

                Widget.Add(Widget.Create("nested"));

                // The inner handle must not commit its caller's transaction.
                Assert.Equal(0, await inner.SaveChangesAsync());
            }

            // Disposing the inner handle left the outer context alive and its changes pending.
            Assert.Equal(0, await Widget.CountAsync());
            Assert.Equal(1, await outer.SaveChangesAsync());
        }

        Assert.Equal(1, await Widget.CountAsync());
    }

    [Fact]
    public async Task A_query_inside_a_unit_of_work_joins_it_rather_than_opening_a_second_context()
    {
        await using var uow = Db.Begin();

        Widget.Add(Widget.Create("pending"));
        await uow.SaveChangesAsync();

        // Reading through the same unit of work sees the write; a second context would too, but only
        // after the commit — what this pins is that the read did not open one.
        Assert.Same(uow.Context, Db.Current);
        Assert.Equal(1, await Widget.CountAsync());
    }

    [Fact]
    public async Task Domain_events_still_publish_through_the_interceptor()
    {
        await using (var uow = Db.Begin())
        {
            Widget.Add(Widget.Create("evented"));
            await uow.SaveChangesAsync();
        }

        Assert.Contains(_recorder.Events, e => e is WidgetCreated);
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
    public async Task Db_Set_reaches_the_ambient_context_directly()
    {
        await using var uow = Db.Begin();

        Db.Set<Widget>().Add(Widget.Create("via-db-set"));
        await uow.SaveChangesAsync();

        Assert.Equal(1, await Db.Set<Widget>().CountAsync());
    }

    [Fact]
    public void Current_without_a_unit_of_work_explains_how_to_open_one()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Db.Current);

        Assert.Contains("Db.Begin()", error.Message, StringComparison.Ordinal);
    }

    private static async Task SeedAsync(params string[] names)
    {
        await using var uow = Db.Begin();
        Widget.AddRange(names.Select(Widget.Create));
        await uow.SaveChangesAsync();
    }
}
