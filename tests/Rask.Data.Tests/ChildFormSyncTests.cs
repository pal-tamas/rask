using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
/// The generated form model carries the aggregate's children, and a save makes the stored ones match it.
/// </summary>
/// <remarks>
/// The rule under test is the sharp one: <b>what the posted list holds is what the aggregate holds afterwards</b>.
/// A row with no id is added, a row whose id matches a stored child updates it, and a stored child nobody posted
/// is removed.
/// </remarks>
[Collection(DataDbCollection.Name)]
public sealed class ChildFormSyncTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-sync-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task The_model_carries_the_children_with_their_ids()
    {
        await using var database = await StartDatabaseAsync();
        var id = await StockedBasketAsync(database);

        var model = (await database.LoadAsync<Basket>(id))!.ToModel();

        Assert.Equal("ada", model.Customer);
        Assert.Equal(
            [("apple", 3), ("pear", 1)],
            model.Lines.OrderBy(l => l.Product, StringComparer.Ordinal).Select(l => (l.Product!, l.Quantity!.Value)));

        // Every stored row says which one it is, or a save could not tell an edit from an insert.
        Assert.All(model.Lines, line => Assert.NotNull(line.Id));
    }

    [Fact]
    public async Task An_edited_row_updates_the_child_it_names()
    {
        await using var database = await StartDatabaseAsync();
        var id = await StockedBasketAsync(database);

        var model = (await database.LoadAsync<Basket>(id))!.ToModel();
        model.Lines.Single(l => l.Product == "apple").Quantity = 9;

        await Basket.UpdateAsync(id, model);

        var after = (await database.LoadAsync<Basket>(id))!;
        Assert.Equal(9, after.Lines.Single(l => l.Product == "apple").Quantity);
        Assert.Equal(2, after.Lines.Count);
    }

    [Fact]
    public async Task A_row_with_no_id_is_added()
    {
        await using var database = await StartDatabaseAsync();
        var id = await StockedBasketAsync(database);

        var model = (await database.LoadAsync<Basket>(id))!.ToModel();
        model.Lines.Add(new BasketLineModel { Product = "plum", Quantity = 4 });

        await Basket.UpdateAsync(id, model);

        var after = (await database.LoadAsync<Basket>(id))!;
        Assert.Equal(["apple", "pear", "plum"], after.Lines.Select(l => l.Product).Order(StringComparer.Ordinal));
        Assert.NotEqual(Guid.Empty, after.Lines.Single(l => l.Product == "plum").Id);
    }

    [Fact]
    public async Task A_row_the_form_no_longer_holds_is_removed()
    {
        await using var database = await StartDatabaseAsync();
        var id = await StockedBasketAsync(database);

        var model = (await database.LoadAsync<Basket>(id))!.ToModel();
        model.Lines.RemoveAll(l => l.Product == "pear");

        await Basket.UpdateAsync(id, model);

        Assert.Equal(["apple"], (await database.LoadAsync<Basket>(id))!.Lines.Select(l => l.Product));
    }

    /// <summary>The documented edge of the rule the owner chose: an empty list empties the aggregate.</summary>
    [Fact]
    public async Task An_empty_list_removes_every_child()
    {
        await using var database = await StartDatabaseAsync();
        var id = await StockedBasketAsync(database);

        var model = (await database.LoadAsync<Basket>(id))!.ToModel();
        model.Lines.Clear();

        await Basket.UpdateAsync(id, model);

        Assert.Empty((await database.LoadAsync<Basket>(id))!.Lines);
    }

    /// <summary>
    /// A posted id that matches nothing in THIS aggregate adds a row rather than reaching one — so a forged id
    /// cannot move another basket's line, and cannot delete it either.
    /// </summary>
    [Fact]
    public async Task An_id_from_another_aggregate_is_treated_as_a_new_row()
    {
        await using var database = await StartDatabaseAsync();
        var mine = await StockedBasketAsync(database);
        var theirs = await StockedBasketAsync(database, "grace");

        var stolen = (await database.LoadAsync<Basket>(theirs))!.Lines.First().Id;

        var model = (await database.LoadAsync<Basket>(mine))!.ToModel();
        model.Lines.Clear();
        model.Lines.Add(new BasketLineModel { Id = stolen, Product = "forged", Quantity = 1 });

        await Basket.UpdateAsync(mine, model);

        var updated = (await database.LoadAsync<Basket>(mine))!;
        var untouched = (await database.LoadAsync<Basket>(theirs))!;

        // The forged row landed as a NEW line, with a new id of its own.
        Assert.Equal(["forged"], updated.Lines.Select(l => l.Product));
        Assert.NotEqual(stolen, updated.Lines.Single().Id);

        // And the other basket still has both of its own lines.
        Assert.Equal(2, untouched.Lines.Count);
    }

    /// <summary>A model save is still a change to the aggregate, so its version moves.</summary>
    [Fact]
    public async Task Saving_children_through_the_model_bumps_the_root()
    {
        await using var database = await StartDatabaseAsync();
        var id = await StockedBasketAsync(database);

        var before = (await database.LoadAsync<Basket>(id))!;
        var model = before.ToModel();
        model.Lines.Single(l => l.Product == "pear").Quantity = 7;

        await Basket.UpdateAsync(id, model);

        Assert.Equal(before.Version + 1, (await database.LoadAsync<Basket>(id))!.Version);
    }

    private async Task<Guid> StockedBasketAsync(TestDatabase database, string customer = "ada")
    {
        var basket = Basket.Open(customer);
        basket.Add("apple", 3);
        basket.Add("pear", 1);

        database.Context.Add(basket);
        await database.Context.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();

        return basket.Id;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"));
}
