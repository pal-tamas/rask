using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

/// <summary>
///     Inside a <c>Db.Transaction</c>, an aggregate's generated entries (<c>HayLoad.Deliver(…)</c>) and its set writes
///     (<c>HayBale.DeleteWhere(…)</c>, <c>UpdateWhere(…)</c>) join that transaction — beside what the app saves on its
///     own context — so a load of several writes commits once or not at all. Outside one, each commits on its own.
/// </summary>
public sealed class DbTransactionJoinedWritesTests : IDisposable
{
    private const string OneLoad = "A load and its bales are only valid together";

    private readonly InMemorySqlite _sqlite = new();
    private readonly SavedTypes _saved = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task A_factory_entry_inside_a_transaction_that_later_throws_writes_nothing()
    {
        await using var database = await StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await HayLoad.Deliver("North", 12, Token);
            throw new InvalidOperationException("Something after the entry failed.");
        }, cancellationToken: Token));

        Assert.Equal(0, await database.Context.Set<HayLoad>().CountAsync(Token));
    }

    [Fact]
    public async Task A_factory_entry_inside_a_transaction_is_seen_by_a_later_read_in_the_same_body()
    {
        await using var database = await StartAsync();
        (int Read, int AppContext) seen = default;

        await Db.Transaction(async () =>
        {
            var id = await HayLoad.Deliver("North", 12, Token);
            seen = (await HayLoad.Count(Token), await database.Context.Set<HayLoad>().CountAsync(l => l.Id == id, Token));
        }, cancellationToken: Token);

        Assert.Equal((1, 1), seen);
    }

    [Fact]
    public async Task Factory_entries_in_a_loop_inside_one_transaction_commit_as_that_one_transaction()
    {
        await using var database = await StartAsync();
        using var recorded = Db.Record();

        await Db.Transaction(async () =>
        {
            foreach (var meadow in new[] { "North", "South", "East" })
            {
                await HayLoad.Deliver(meadow, 12, Token);
            }
        }, manyAggregates: "One day's loads are booked together", cancellationToken: Token);

        Assert.Equal(3, Assert.Single(recorded.Transactions).Aggregates.Count);
        Assert.Equal(3, await database.Context.Set<HayLoad>().CountAsync(Token));
    }

    [Fact]
    public async Task Factory_entries_in_a_loop_inside_one_transaction_write_nothing_when_the_last_is_refused()
    {
        await using var database = await StartAsync();

        await Assert.ThrowsAsync<RaskValidationException>(() => Db.Transaction(async () =>
        {
            foreach (var bales in new[] { 12, 30, 500 })
            {
                await HayLoad.Deliver("North", bales, Token);
            }
        }, manyAggregates: "One day's loads are booked together", cancellationToken: Token));

        Assert.Equal(0, await database.Context.Set<HayLoad>().CountAsync(Token));
    }

    [Fact]
    public async Task Factory_entries_in_a_loop_count_toward_the_one_aggregate_rule_of_the_transaction_they_join()
    {
        await using var database = await StartAsync();

        var refused = await Assert.ThrowsAsync<ManyAggregatesException>(() => Db.Transaction(async () =>
        {
            await HayLoad.Deliver("North", 12, Token);
            await HayLoad.Deliver("South", 12, Token);
        }, cancellationToken: Token));

        Assert.Equal(2, refused.Aggregates.Count);
        Assert.Equal(0, await database.Context.Set<HayLoad>().CountAsync(Token));
    }

    [Fact]
    public async Task A_factory_entrys_id_is_there_for_the_rows_the_app_adds_after_it_in_the_same_body()
    {
        await using var database = await StartAsync();
        using var recorded = Db.Record();

        var id = await Db.Transaction(async () =>
        {
            var load = await HayLoad.Deliver("North", 2, Token);
            database.Context.AddRange(HayBale.Of(load, "n-1"), HayBale.Of(load, "n-2"));
            await database.Context.SaveChangesAsync(Token);
            return load;
        }, manyAggregates: OneLoad, cancellationToken: Token);

        Assert.NotEqual(default, id);
        Assert.Equal(["n-1", "n-2"], await TagsAsync(database, id));
        Assert.Equal(3, Assert.Single(recorded.Transactions).Aggregates.Count);
    }

    [Fact]
    public async Task DeleteWhere_then_rows_saved_on_the_apps_context_commit_together_under_serializable()
    {
        await using var database = await StartAsync();
        var load = await HayLoad.Deliver("North", 2, Token);
        await BalesAsync(database, load, "old-1", "old-2");
        using var recorded = Db.Record();

        await Db.Transaction(async () =>
        {
            await HayBale.DeleteWhere(b => b.Load == load, Token);
            database.Context.AddRange(HayBale.Of(load, "new-1"), HayBale.Of(load, "new-2"));
            await database.Context.SaveChangesAsync(Token);
        }, manyAggregates: OneLoad, IsolationLevel.Serializable, Token);

        Assert.Equal(["new-1", "new-2"], await TagsAsync(database, load));
        Assert.Contains("HayBale (set)", Assert.Single(recorded.Transactions).Aggregates);
    }

    [Fact]
    public async Task DeleteWhere_then_rows_saved_on_the_apps_context_roll_back_together_when_the_body_throws()
    {
        await using var database = await StartAsync();
        var load = await HayLoad.Deliver("North", 2, Token);
        await BalesAsync(database, load, "old-1", "old-2");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await HayBale.DeleteWhere(b => b.Load == load, Token);
            database.Context.AddRange(HayBale.Of(load, "new-1"), HayBale.Of(load, "new-2"));
            await database.Context.SaveChangesAsync(Token);
            throw new InvalidOperationException("The file's last line failed.");
        }, manyAggregates: OneLoad, IsolationLevel.Serializable, Token));

        database.Context.ChangeTracker.Clear();
        Assert.Equal(["old-1", "old-2"], await TagsAsync(database, load));
    }

    [Fact]
    public async Task UpdateWhere_inside_a_transaction_bumps_the_version_and_rolls_back_with_it()
    {
        await using var database = await StartAsync();
        var load = await HayLoad.Deliver("North", 2, Token);
        await BalesAsync(database, load, "old-1");
        var versionInside = -1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await HayBale.UpdateWhere(b => b.Load == load, s => s.SetProperty(b => b.Tag, "relabelled"), Token);
            versionInside = await database.Context.Set<HayBale>().AsNoTracking().Select(b => b.Version).SingleAsync(Token);
            throw new InvalidOperationException("Something after the update failed.");
        }, cancellationToken: Token));

        var stored = await database.Context.Set<HayBale>().AsNoTracking().SingleAsync(Token);
        Assert.Equal(1, versionInside);
        Assert.Equal(("old-1", 0), (stored.Tag, stored.Version));
    }

    [Fact]
    public async Task The_notices_of_entries_and_set_writes_inside_a_transaction_wait_for_its_commit()
    {
        await using var database = await StartAsync();
        var load = await HayLoad.Deliver("North", 2, Token);
        await BalesAsync(database, load, "old-1");
        _saved.Clear();
        var heardInside = -1;

        await Db.Transaction(async () =>
        {
            await HayLoad.Deliver("South", 3, Token);
            await HayBale.DeleteWhere(b => b.Load == load, Token);
            heardInside = _saved.Count;
        }, manyAggregates: OneLoad, cancellationToken: Token);

        Assert.Equal(0, heardInside);
        Assert.Equal([typeof(HayBale), typeof(HayLoad)], _saved.Distinct());
    }

    [Fact]
    public async Task The_notices_of_entries_and_set_writes_inside_a_transaction_never_go_out_on_a_rollback()
    {
        await using var database = await StartAsync();
        var load = await HayLoad.Deliver("North", 2, Token);
        await BalesAsync(database, load, "old-1");
        _saved.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await HayLoad.Deliver("South", 3, Token);
            await HayBale.DeleteWhere(b => b.Load == load, Token);
            throw new InvalidOperationException("Something after the writes failed.");
        }, manyAggregates: OneLoad, cancellationToken: Token));

        Assert.Equal(0, _saved.Count);
    }

    [Fact]
    public async Task Outside_any_transaction_each_entry_and_set_write_commits_and_is_told_on_its_own()
    {
        await using var database = await StartAsync();
        using var recorded = Db.Record();

        var load = await HayLoad.Deliver("North", 2, Token);
        var heardAfterEntry = _saved.Count;
        await BalesAsync(database, load, "old-1");
        await HayBale.DeleteWhere(b => b.Load == load, Token);

        // The plain save between them is no Db.Transaction, so the recording counts the entry's and the set write's own.
        Assert.Equal(1, heardAfterEntry);
        Assert.Equal(2, recorded.Transactions.Count);
        Assert.Empty(await TagsAsync(database, load));
    }

    private static async Task BalesAsync(TestDatabase database, HayLoadId load, params string[] tags)
    {
        database.Context.AddRange(tags.Select(tag => HayBale.Of(load, tag)));
        await database.Context.SaveChangesAsync(Token);
    }

    private static Task<List<string>> TagsAsync(TestDatabase database, HayLoadId load) =>
        database.Context.Set<HayBale>().AsNoTracking().Where(b => b.Load == load).OrderBy(b => b.Tag).Select(b => b.Tag).ToListAsync(Token);

    private Task<TestDatabase> StartAsync() =>
        TestDatabase.Start(
            o => o.UseSqlite(_sqlite.ConnectionString),
            services: s => s.AddSingleton<ISavedTypeWatcher>(_saved),
            cancellationToken: Token);

    // What the host hears once a write commits: the types a live page showing them would repaint on.
    private sealed class SavedTypes : ISavedTypeWatcher
    {
        private readonly List<Type> _types = [];

        public int Count
        {
            get
            {
                lock (_types)
                {
                    return _types.Count;
                }
            }
        }

        public bool Watches(Type aggregate) => true;

        public void Saved(IReadOnlyCollection<Type> aggregates)
        {
            lock (_types)
            {
                _types.AddRange(aggregates);
            }
        }

        public void Clear()
        {
            lock (_types)
            {
                _types.Clear();
            }
        }

        public List<Type> Distinct()
        {
            lock (_types)
            {
                return [.. _types.Distinct().OrderBy(t => t.Name, StringComparer.Ordinal)];
            }
        }
    }
}
