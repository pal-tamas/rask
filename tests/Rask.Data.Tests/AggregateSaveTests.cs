using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>An aggregate with no Version that soft-deletes, holding a payload that may be large.</summary>
public sealed class Satchel : Aggregate<Guid, Model, SoftDelete>
{
    private Satchel() { } // EF materialization

    public string Name { get; private set; } = "";

    public byte[] Contents { get; private set; } = [];

    public static Satchel Holding(string name, byte[] contents) =>
        new() { Id = Guid.CreateVersion7(), Name = name, Contents = contents };

    public void Rename(string name) => Name = name;
}

// `Product.Find(id)` → its own methods → `product.Save()`: the aggregate loaded, changed and written back with
// no context held in between, against a real SQLite file with the fixture's interceptors.
[Collection(DataDbCollection.Name)]
public sealed class AggregateSaveTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-save-{Guid.NewGuid():N}.db");
    private readonly FakeClock _clock = new(Start);
    private readonly SqlCommandLog _log = new();

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Find_loads_the_aggregate_with_its_children()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveBasketAsync();

        var basket = await Basket.Find(id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(basket);
        Assert.Equal(["apple", "pear"], basket.Lines.Select(l => l.Product).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Find_does_not_return_a_soft_deleted_aggregate()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
        await Widget.Delete(widget.Id, cancellationToken: TestContext.Current.CancellationToken);

        var found = await Widget.Find(widget.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(found);
    }

    [Fact]
    public async Task Save_writes_the_change_and_refreshes_the_version_it_holds()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
        var found = (await Widget.Find(widget.Id, cancellationToken: TestContext.Current.CancellationToken))!;

        found.Rename("hammer");
        await found.Save(cancellationToken: TestContext.Current.CancellationToken);
        found.Rename("mallet");
        await found.Save(cancellationToken: TestContext.Current.CancellationToken);

        var stored = await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken);
        Assert.Equal("mallet", stored!.Name);
        Assert.Equal(2, stored.Version);
        Assert.Equal(2, found.Version);
    }

    [Fact]
    public async Task Save_writes_only_the_columns_that_changed()
    {
        // Another writer renames the order between Find and Save. Writing only the cancel keeps their rename.
        await using var database = await StartDatabaseAsync();
        var order = await GeneratedModelWrites.Create(Order.Place("A-1"), cancellationToken: TestContext.Current.CancellationToken);
        var found = (await Order.Find(order.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        await database.Context.Set<Order>()
            .Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Reference, "A-1-renamed"), cancellationToken: TestContext.Current.CancellationToken);

        found.Cancel(Start.UtcDateTime);
        await found.Save(cancellationToken: TestContext.Current.CancellationToken);

        var stored = await database.Load<Order>(order.Id, TestContext.Current.CancellationToken);
        Assert.Equal(OrderStatus.Cancelled, stored!.Status);
        Assert.Equal("A-1-renamed", stored.Reference);
    }

    [Fact]
    public async Task Save_inserts_new_children_updates_changed_ones_and_deletes_removed_ones()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveBasketAsync();
        var basket = (await Basket.Find(id, cancellationToken: TestContext.Current.CancellationToken))!;

        basket.Lines.First(l => l.Product == "apple").SetQuantity(9);
        basket.Remove(basket.Lines.First(l => l.Product == "pear"));
        basket.Add("plum", 2);
        await basket.Save(cancellationToken: TestContext.Current.CancellationToken);

        var stored = await database.Load<Basket>(id, TestContext.Current.CancellationToken);
        Assert.Equal(
            [("apple", 9), ("plum", 2)],
            stored!.Lines.OrderBy(l => l.Product, StringComparer.Ordinal).Select(l => (l.Product, l.Quantity)));
    }

    [Fact]
    public async Task Save_of_a_stale_copy_is_refused_and_the_other_writers_change_stays()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
        var mine = (await Widget.Find(widget.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        var theirs = (await Widget.Find(widget.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        theirs.Rename("theirs");
        await theirs.Save(cancellationToken: TestContext.Current.CancellationToken);

        mine.Rename("mine");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => mine.Save(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("theirs", (await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken))!.Name);
    }

    [Fact]
    public async Task Save_of_an_aggregate_with_no_row_yet_inserts_it_whole()
    {
        await using var database = await StartDatabaseAsync();
        var basket = Basket.Open("ada");
        basket.Add("apple", 3);

        await basket.Save(cancellationToken: TestContext.Current.CancellationToken);

        var stored = await database.Load<Basket>(basket.Id, TestContext.Current.CancellationToken);
        Assert.Equal("ada", stored!.Customer);
        Assert.Equal([("apple", 3)], stored.Lines.Select(l => (l.Product, l.Quantity)));
        Assert.Equal(Start.UtcDateTime, stored.CreatedAt);
    }

    [Fact]
    public async Task Save_of_a_versioned_row_soft_deleted_since_it_was_found_is_a_concurrency_conflict()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
        var found = (await Widget.Find(widget.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        await Widget.Delete(widget.Id, cancellationToken: TestContext.Current.CancellationToken);

        found.Rename("hammer");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => found.Save(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("anvil", Assert.Single(await Widget.IgnoreQueryFilters()).Name);
    }

    [Fact]
    public async Task Save_of_an_unversioned_row_soft_deleted_since_it_was_found_throws_KeyNotFound()
    {
        await using var database = await StartDatabaseAsync();
        var satchel = Satchel.Holding("kit", [1, 2, 3]);
        await satchel.Save(cancellationToken: TestContext.Current.CancellationToken);
        var found = (await Satchel.Find(satchel.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        await Satchel.Delete(satchel.Id, cancellationToken: TestContext.Current.CancellationToken);

        found.Rename("bag");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => found.Save(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("kit", Assert.Single(await Satchel.IgnoreQueryFilters()).Name);
    }

    [Fact]
    public async Task Save_of_a_row_deleted_outright_since_it_was_found_is_a_concurrency_conflict_not_an_insert()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
        var found = (await Widget.Find(widget.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        await database.Context.Set<Widget>().IgnoreQueryFilters().Where(w => w.Id == widget.Id)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        found.Rename("hammer");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => found.Save(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(await Widget.IgnoreQueryFilters());
    }

    [Fact]
    public async Task Save_of_a_child_deleted_by_someone_else_since_it_was_found_is_a_conflict_not_a_reinsert()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveBasketAsync();
        var basket = (await Basket.Find(id, cancellationToken: TestContext.Current.CancellationToken))!;
        await database.Context.Set<BasketLine>().Where(l => l.Product == "pear")
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        basket.Lines.First(l => l.Product == "pear").SetQuantity(5);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => basket.Save(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(["apple"], (await database.Load<Basket>(id, TestContext.Current.CancellationToken))!.Lines.Select(l => l.Product));
    }

    [Fact]
    public async Task Save_through_a_given_context_only_stages_until_the_caller_saves()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
        var found = (await Widget.Find(widget.Id, cancellationToken: TestContext.Current.CancellationToken))!;

        found.Rename("hammer");
        await found.Save(database.Context, TestContext.Current.CancellationToken);
        var beforeCommit = (await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken))!.Name;
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("anvil", beforeCommit);
        Assert.Equal("hammer", (await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken))!.Name);
    }

    [Fact]
    public async Task Find_and_Save_read_the_row_once()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
        _log.Clear();

        var found = (await Widget.Find(widget.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        found.Rename("hammer");
        await found.Save(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, Selects());
        Assert.Equal("hammer", (await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken))!.Name);
        Assert.Equal(1, found.Version);
    }

    [Fact]
    public async Task Find_Save_and_Save_again_read_the_row_once()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
        _log.Clear();

        var found = (await Widget.Find(widget.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        found.Rename("hammer");
        await found.Save(cancellationToken: TestContext.Current.CancellationToken);
        found.Rename("mallet");
        await found.Save(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, Selects());
        Assert.Equal("mallet", (await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken))!.Name);
        Assert.Equal(2, found.Version);
    }

    [Fact]
    public async Task A_sixteen_megabyte_row_is_read_once_by_Find_and_never_by_Save()
    {
        await using var database = await StartDatabaseAsync();
        var satchel = Satchel.Holding("kit", new byte[16 * 1024 * 1024]);
        await satchel.Save(cancellationToken: TestContext.Current.CancellationToken);
        _log.Clear();

        var found = (await Satchel.Find(satchel.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        found.Rename("bag");
        await found.Save(cancellationToken: TestContext.Current.CancellationToken);

        // The other SELECT asks whether the unversioned row is still there, by its key alone.
        Assert.Equal(1, _log.Texts.Count(t => t.Contains("\"Contents\"", StringComparison.Ordinal) && t.TrimStart().StartsWith("SELECT", StringComparison.Ordinal)));
        Assert.Equal(2, Selects());
        Assert.Equal("bag", (await database.Load<Satchel>(satchel.Id, TestContext.Current.CancellationToken))!.Name);
    }

    [Fact]
    public async Task A_save_staged_on_a_given_context_and_committed_leaves_the_next_save_read_free()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
        var found = (await Widget.Find(widget.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        found.Rename("hammer");
        await found.Save(database.Context, TestContext.Current.CancellationToken);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.Context.ChangeTracker.Clear();
        _log.Clear();

        found.Rename("mallet");
        await found.Save(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, Selects());
        Assert.Equal("mallet", (await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken))!.Name);
    }

    [Fact]
    public async Task An_aggregate_built_by_hand_with_the_key_of_a_stored_row_is_an_insert_and_is_refused()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
        var copy = Widget.Create("copy");
        typeof(Entity<Guid>).GetProperty(nameof(Widget.Id))!.SetValue(copy, widget.Id);
        copy.ClearDomainEvents();

        await Assert.ThrowsAsync<DbUpdateException>(() => copy.Save(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("anvil", (await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken))!.Name);
    }

    [Fact]
    public async Task Save_of_an_aggregate_its_factory_just_made_inserts_it_with_no_read_first()
    {
        await using var database = await StartDatabaseAsync();
        var basket = Basket.Open("ada");
        basket.Add("apple", 3);
        _log.Clear();

        await basket.Save(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, Selects());
        Assert.Equal([("apple", 3)], (await database.Load<Basket>(basket.Id, TestContext.Current.CancellationToken))!
            .Lines.Select(l => (l.Product, l.Quantity)));
    }

    [Fact]
    public async Task The_next_save_of_an_aggregate_its_factory_made_updates_the_row_the_first_inserted()
    {
        await using var database = await StartDatabaseAsync();
        var basket = Basket.Open("ada");
        await basket.Save(cancellationToken: TestContext.Current.CancellationToken);

        basket.Add("pear", 1);
        await basket.Save(cancellationToken: TestContext.Current.CancellationToken);

        var stored = await database.Load<Basket>(basket.Id, TestContext.Current.CancellationToken);
        Assert.Equal([("pear", 1)], stored!.Lines.Select(l => (l.Product, l.Quantity)));
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task A_new_aggregate_staged_on_a_given_context_and_committed_is_updated_by_its_next_save()
    {
        await using var database = await StartDatabaseAsync();
        var widget = Widget.Create("anvil");
        await widget.Save(database.Context, TestContext.Current.CancellationToken);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.Context.ChangeTracker.Clear();

        widget.Rename("hammer");
        await widget.Save(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("hammer", (await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken))!.Name);
        Assert.Single(await Widget.IgnoreQueryFilters());
    }

    private int Selects() =>
        _log.Texts.Count(t => t.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase));

    private static async Task<Guid> SaveBasketAsync()
    {
        var basket = Basket.Open("ada");
        basket.Add("apple", 3);
        basket.Add("pear", 1);
        await basket.Save();
        return basket.Id;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}").AddInterceptors(_log), _clock);
}
