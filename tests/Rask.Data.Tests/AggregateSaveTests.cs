using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// `Product.Find(id)` → its own methods → `product.Save()`: the aggregate loaded, changed and written back with
// no context held in between, against a real SQLite file with the fixture's interceptors.
[Collection(DataDbCollection.Name)]
public sealed class AggregateSaveTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-save-{Guid.NewGuid():N}.db");
    private readonly FakeClock _clock = new(Start);

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Find_loads_the_aggregate_with_its_children()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveBasketAsync();

        var basket = await Basket.Find(id);

        Assert.NotNull(basket);
        Assert.Equal(["apple", "pear"], basket.Lines.Select(l => l.Product).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Find_does_not_return_a_soft_deleted_aggregate()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"));
        await Widget.Delete(widget.Id);

        var found = await Widget.Find(widget.Id);

        Assert.Null(found);
    }

    [Fact]
    public async Task Save_writes_the_change_and_refreshes_the_version_it_holds()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"));
        var found = (await Widget.Find(widget.Id))!;

        found.Rename("hammer");
        await found.Save();
        found.Rename("mallet");
        await found.Save();

        var stored = await database.LoadAsync<Widget>(widget.Id);
        Assert.Equal("mallet", stored!.Name);
        Assert.Equal(2, stored.Version);
        Assert.Equal(2, found.Version);
    }

    [Fact]
    public async Task Save_writes_only_the_columns_that_changed()
    {
        // Another writer renames the order between Find and Save. Writing only the cancel keeps their rename.
        await using var database = await StartDatabaseAsync();
        var order = await GeneratedModelWrites.Create(Order.Place("A-1"));
        var found = (await Order.Find(order.Id))!;
        await database.Context.Set<Order>()
            .Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Reference, "A-1-renamed"));

        found.Cancel(Start.UtcDateTime);
        await found.Save();

        var stored = await database.LoadAsync<Order>(order.Id);
        Assert.Equal(OrderStatus.Cancelled, stored!.Status);
        Assert.Equal("A-1-renamed", stored.Reference);
    }

    [Fact]
    public async Task Save_inserts_new_children_updates_changed_ones_and_deletes_removed_ones()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveBasketAsync();
        var basket = (await Basket.Find(id))!;

        basket.Lines.First(l => l.Product == "apple").SetQuantity(9);
        basket.Remove(basket.Lines.First(l => l.Product == "pear"));
        basket.Add("plum", 2);
        await basket.Save();

        var stored = await database.LoadAsync<Basket>(id);
        Assert.Equal(
            [("apple", 9), ("plum", 2)],
            stored!.Lines.OrderBy(l => l.Product, StringComparer.Ordinal).Select(l => (l.Product, l.Quantity)));
    }

    [Fact]
    public async Task Save_of_a_stale_copy_is_refused_and_the_other_writers_change_stays()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"));
        var mine = (await Widget.Find(widget.Id))!;
        var theirs = (await Widget.Find(widget.Id))!;
        theirs.Rename("theirs");
        await theirs.Save();

        mine.Rename("mine");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => mine.Save());

        Assert.Equal("theirs", (await database.LoadAsync<Widget>(widget.Id))!.Name);
    }

    [Fact]
    public async Task Save_of_an_aggregate_with_no_row_yet_inserts_it_whole()
    {
        await using var database = await StartDatabaseAsync();
        var basket = Basket.Open("ada");
        basket.Add("apple", 3);

        await basket.Save();

        var stored = await database.LoadAsync<Basket>(basket.Id);
        Assert.Equal("ada", stored!.Customer);
        Assert.Equal([("apple", 3)], stored.Lines.Select(l => (l.Product, l.Quantity)));
        Assert.Equal(Start.UtcDateTime, stored.CreatedAt);
    }

    [Fact]
    public async Task Save_of_a_row_deleted_since_it_was_found_throws_KeyNotFound()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"));
        var found = (await Widget.Find(widget.Id))!;
        await Widget.Delete(widget.Id);

        found.Rename("hammer");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => found.Save());

        Assert.Single(await Widget.IgnoreQueryFilters());
    }

    [Fact]
    public async Task Save_through_a_given_context_only_stages_until_the_caller_saves()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"));
        var found = (await Widget.Find(widget.Id))!;

        found.Rename("hammer");
        await found.Save(database.Context);
        var beforeCommit = (await database.LoadAsync<Widget>(widget.Id))!.Name;
        await database.Context.SaveChangesAsync();

        Assert.Equal("anvil", beforeCommit);
        Assert.Equal("hammer", (await database.LoadAsync<Widget>(widget.Id))!.Name);
    }

    private static async Task<Guid> SaveBasketAsync()
    {
        var basket = Basket.Open("ada");
        basket.Add("apple", 3);
        basket.Add("pear", 1);
        await basket.Save();
        return basket.Id;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"), _clock);
}
