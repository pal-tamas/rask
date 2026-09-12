using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// Product.AsQueryable() is the one read that outlives a call: a standard IQueryable that a component
// composes its own LINQ on — UiDataGrid counts, orders, pages and lists it — holding no context between
// executions. What that promise rests on is pinned here: the LINQ an IQueryable consumer actually calls
// runs in the database, every execution sees the database as it is now, and every context an execution
// opens is released again — synchronous or awaited, succeeding or failing.
[Collection(DataDbCollection.Name)]
public sealed class ModelQueryableTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-queryable-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    // ---- what a consumer calls --------------------------------------------------------------------

    [Fact]
    public async Task The_grids_count_order_page_and_list_sequence_returns_the_right_page()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database, "echo", "alpha", "delta", "bravo", "charlie");

        // The grid's own shape: the sort key is boxed to object, which EF Core has to see through.
        Expression<Func<Widget, object?>> byName = w => w.Name;
        Expression<Func<Widget, object?>> byId = w => w.Id;

        var query = Widget.AsQueryable();
        var total = query.Count();
        var rows = query.OrderBy(byName).ThenBy(byId).Skip(2).Take(2).ToList();
        var descending = query.OrderByDescending(byName).Skip(0).Take(2).ToList();

        Assert.Equal(5, total);
        Assert.Equal(["charlie", "delta"], rows.Select(w => w.Name));
        Assert.Equal(["echo", "delta"], descending.Select(w => w.Name));

        // And the footer's second, unpaged read of the same queryable.
        Assert.Equal(5, query.ToList().Count);
    }

    [Fact]
    public async Task One_queryable_run_again_sees_the_database_as_it_is_now()
    {
        // Composed once and kept, the way a page keeps it in a field. A queryable over one long-lived
        // context would answer the second run from that context's identity map — the renamed row would
        // come back under its old name.
        await using var database = await StartDatabaseAsync();
        var query = Widget.AsQueryable().Where(w => w.Name != "hidden");

        Assert.Equal(0, query.Count());

        var first = await GeneratedModelWrites.CreateAsync(Widget.Create("first"));
        Assert.Equal(["first"], query.ToList().Select(w => w.Name));

        await GeneratedModelWrites.CreateAsync(Widget.Create("second"));
        await GeneratedModelWrites.CreateAsync(Widget.Create("hidden"));
        await GeneratedModelWrites.UpdateAsync<Widget>(first.Id, version: null, w => w.Rename("renamed"));

        Assert.Equal(["renamed", "second"], query.OrderBy(w => w.Name).ToList().Select(w => w.Name));
        Assert.Equal(2, await query.CountAsync());
    }

    [Fact]
    public async Task EF_Cores_awaited_operators_run_on_it()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database, "alpha", "bravo", "charlie");

        var query = Widget.AsQueryable();

        Assert.Equal(3, (await query.ToListAsync()).Count);
        Assert.Equal(3, await query.CountAsync());
        Assert.True(await query.AnyAsync(w => w.Name == "bravo"));
        Assert.Equal("alpha", (await query.OrderBy(w => w.Name).FirstOrDefaultAsync())!.Name);
        Assert.Null(await query.FirstOrDefaultAsync(w => w.Name == "nope"));

        var streamed = new List<string>();
        await foreach (var widget in query.OrderByDescending(w => w.Name).AsAsyncEnumerable())
        {
            streamed.Add(widget.Name);
        }

        Assert.Equal(["charlie", "bravo", "alpha"], streamed);
    }

    [Fact]
    public async Task EF_Cores_own_operators_travel_when_put_on_the_model_query_first()
    {
        await using var database = await StartDatabaseAsync();
        var (gone, _) = await SeedAsync(database, "gone", "live");

        database.Context.Remove(gone);
        await database.Context.SaveChangesAsync();

        Assert.Equal(1, Widget.AsQueryable().Count());
        Assert.Equal(2, Widget.IgnoreQueryFilters().AsQueryable().Count());
        Assert.Equal(2, await Widget.IgnoreQueryFilters().AsQueryable().CountAsync());
        Assert.Equal(1, Widget.Where(w => w.Name == "gone").IgnoreQueryFilters().AsQueryable().Count());
    }

    [Fact]
    public async Task A_LINQ_projection_runs_on_it()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database, "bravo", "alpha");

        var query = Widget.AsQueryable();

        Assert.Equal(["alpha", "bravo"], query.OrderBy(w => w.Name).Select(w => w.Name).ToList());
        Assert.Equal(["alpha", "bravo"], await query.OrderBy(w => w.Name).Select(w => w.Name).ToListAsync());
    }

    [Fact]
    public void The_non_generic_CreateQuery_is_refused_rather_than_guessed_at()
    {
        var query = Widget.AsQueryable();

        Assert.Throws<NotSupportedException>(() => query.Provider.CreateQuery(query.Expression));
    }

    // ---- every context released -------------------------------------------------------------------

    [Fact]
    public async Task A_synchronous_execution_releases_the_context_it_opened()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database, "alpha", "bravo", "charlie");
        var tally = ConfigureCountingContexts();

        var query = Widget.AsQueryable();

        _ = query.Count();
        _ = query.OrderBy(w => w.Name).Skip(1).Take(1).ToList();
        _ = query.Select(w => w.Name).ToList();
        _ = query.Any(w => w.Name == "bravo");
        using (var rows = query.GetEnumerator())
        {
            Assert.True(rows.MoveNext());
        }

        AssertEveryContextReleased(tally, executions: 5);
    }

    [Fact]
    public async Task An_awaited_execution_releases_the_context_it_opened()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database, "alpha", "bravo", "charlie");
        var tally = ConfigureCountingContexts();

        var query = Widget.AsQueryable();

        _ = await query.ToListAsync();
        _ = await query.CountAsync();
        _ = await query.FirstOrDefaultAsync(w => w.Name == "bravo");

        // A stream abandoned after its first row, which is the case a missing dispose would leak.
        await using (var rows = query.AsAsyncEnumerable().GetAsyncEnumerator())
        {
            Assert.True(await rows.MoveNextAsync());
        }

        AssertEveryContextReleased(tally, executions: 4);
    }

    [Fact]
    public async Task A_failed_execution_still_releases_the_context_it_opened()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database, "alpha");
        var tally = ConfigureCountingContexts();

        var untranslatable = Widget.AsQueryable().Where(w => IsInteresting(w.Name));

        Assert.Throws<InvalidOperationException>(() => untranslatable.Count());
        await Assert.ThrowsAsync<InvalidOperationException>(() => untranslatable.CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => untranslatable.ToListAsync());

        AssertEveryContextReleased(tally, executions: 3);
    }

    // A local method EF Core cannot translate to SQL, so a Where over it fails at execution.
    private static bool IsInteresting(string name) => name.Length > 0;

    // Points Db at contexts that count themselves, over the database the fixture created. TestDatabase's
    // DisposeAsync resets Db, so this never outlives the test.
    private ContextTally ConfigureCountingContexts()
    {
        var tally = new ContextTally();
        var options = new DbContextOptionsBuilder<RaskDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        Db.Configure(() => new DisposalCountingContext(options, tally));
        return tally;
    }

    // An awaited scalar (CountAsync, FirstOrDefaultAsync) releases its context in a continuation on the task
    // the caller awaited, which can land a moment after the await resumed — so the tally is given a bounded
    // moment to settle before it is judged, and fails if it never does.
    private static void AssertEveryContextReleased(ContextTally tally, int executions)
    {
        SpinWait.SpinUntil(() => tally.Released >= tally.Opened, TimeSpan.FromSeconds(5));

        Assert.Equal(executions, tally.Opened);
        Assert.Equal(tally.Opened, tally.Released);
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"));

    private static async Task<(Widget First, Widget Second)> SeedAsync(TestDatabase database, params string[] names)
    {
        var widgets = names.Select(Widget.Create).ToArray();
        database.Context.AddRange(widgets);
        await database.Context.SaveChangesAsync();
        return (widgets[0], widgets.Length > 1 ? widgets[1] : widgets[0]);
    }

    private sealed class ContextTally
    {
        private int _opened;
        private int _released;

        public int Opened => Volatile.Read(ref _opened);

        public int Released => Volatile.Read(ref _released);

        public void Open() => Interlocked.Increment(ref _opened);

        public void Release() => Interlocked.Increment(ref _released);
    }

    // Counts each instance once however it is disposed — EF Core may route DisposeAsync through Dispose.
    private sealed class DisposalCountingContext : RaskDbContext
    {
        private readonly ContextTally _tally;
        private int _released;

        public DisposalCountingContext(DbContextOptions options, ContextTally tally)
            : base(options)
        {
            _tally = tally;
            tally.Open();
        }

        public override void Dispose()
        {
            Release();
            base.Dispose();
        }

        public override ValueTask DisposeAsync()
        {
            Release();
            return base.DisposeAsync();
        }

        private void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _tally.Release();
            }
        }
    }
}
