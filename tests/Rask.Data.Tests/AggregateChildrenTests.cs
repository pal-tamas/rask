using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// The child shape Rask recommends: the line is an Entity<Guid>, NOT an Aggregate<Guid>. It has no version of its
// own, no soft delete and no query surface — it is part of the invoice, and the invoice is what you load and save.
public sealed class Basket : Aggregate<Guid>
{
    private readonly List<BasketLine> _lines = [];

    private Basket() { } // EF materialization

    public string Customer { get; private set; } = "";

    public IReadOnlyCollection<BasketLine> Lines => _lines;

    public static Basket Open(string customer) =>
        new() { Id = Guid.CreateVersion7(), Customer = customer };

    public BasketLine Add(string product, int quantity)
    {
        var line = BasketLine.For(product, quantity);
        _lines.Add(line);
        return line;
    }

    public void Remove(BasketLine line) => _lines.Remove(line);
}

public sealed class BasketLine : Entity<Guid>
{
    private BasketLine() { } // EF materialization

    public string Product { get; private set; } = "";

    public int Quantity { get; private set; }

    public void SetQuantity(int quantity) => Quantity = quantity;

    internal static BasketLine For(string product, int quantity) =>
        new() { Id = Guid.CreateVersion7(), Product = product, Quantity = quantity };
}

/// <summary>
/// What it means for an aggregate to hold children: how it loads, and what a change to a part does to the whole.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class AggregateChildrenTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-invoice-{Guid.NewGuid():N}.db");
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public void Dispose() => File.Delete(_dbPath);

    /// <summary>Loading ONE root by its key loads the aggregate whole — nothing has to say Include.</summary>
    [Fact]
    public async Task Finding_one_root_by_id_loads_its_children()
    {
        await using var database = await StartDatabaseAsync();
        var id = await OpenBasketAsync(database);

        var order = await Basket.FindAsync(id);

        Assert.NotNull(order);
        Assert.Equal(
            [("apple", 3), ("pear", 1)],
            order.Lines.OrderBy(l => l.Product, StringComparer.Ordinal).Select(l => (l.Product, l.Quantity)));
    }

    /// <summary>
    /// A query does not: listing a thousand roots should not drag in everything each of them holds.
    /// </summary>
    [Fact]
    public async Task A_list_query_leaves_the_children_alone()
    {
        await using var database = await StartDatabaseAsync();
        await OpenBasketAsync(database);

        var orders = await Basket.Where(o => o.Customer == "ada").ToListAsync();

        Assert.Single(orders);
        Assert.Empty(orders[0].Lines);
    }

    /// <summary>A line's quantity changing IS the order changing.</summary>
    [Fact]
    public async Task Changing_only_a_child_bumps_the_roots_version_and_timestamp()
    {
        await using var database = await StartDatabaseAsync();
        var id = await OpenBasketAsync(database);

        var before = (await Basket.FindAsync(id))!;
        _clock.UtcNow = _clock.UtcNow.AddHours(1);

        // The root's own columns are untouched: only the line changes.
        await Basket.UpdateAsync(id, o => o.Lines.First(l => l.Product == "apple").SetQuantity(9));

        var after = (await Basket.FindAsync(id))!;

        Assert.Equal(before.Version + 1, after.Version);
        Assert.Equal(_clock.UtcNow.UtcDateTime, after.UpdatedAt);
        Assert.Equal(9, after.Lines.Single(l => l.Product == "apple").Quantity);

        // And the root's own data is still its own — a bump must not write stale columns back.
        Assert.Equal("ada", after.Customer);
    }

    [Fact]
    public async Task Adding_a_child_bumps_the_root()
    {
        await using var database = await StartDatabaseAsync();
        var id = await OpenBasketAsync(database);
        var before = (await Basket.FindAsync(id))!.Version;

        await Basket.UpdateAsync(id, o => o.Add("plum", 2));

        Assert.Equal(before + 1, (await Basket.FindAsync(id))!.Version);
    }

    /// <summary>
    /// A removed child has been taken out of the collection, so the root is found by walking UP from the child.
    /// </summary>
    [Fact]
    public async Task Removing_a_child_bumps_the_root()
    {
        await using var database = await StartDatabaseAsync();
        var id = await OpenBasketAsync(database);
        var before = (await Basket.FindAsync(id))!.Version;

        await Basket.UpdateAsync(id, o => o.Remove(o.Lines.First(l => l.Product == "pear")));

        var after = (await Basket.FindAsync(id))!;

        Assert.Equal(before + 1, after.Version);
        Assert.Equal(["apple"], after.Lines.Select(l => l.Product));
    }

    /// <summary>
    /// A removed child is GONE, not orphaned.
    /// </summary>
    /// <remarks>
    /// The navigation hides an orphan exactly as it hides a deleted row, so asking the aggregate would pass either
    /// way. This counts the rows instead: with an optional foreign key EF Core severs the child by nulling it, and
    /// the line would sit in the table forever, invisible and undeletable.
    /// </remarks>
    [Fact]
    public async Task A_removed_child_leaves_no_row_behind()
    {
        await using var database = await StartDatabaseAsync();
        var id = await OpenBasketAsync(database);

        await Basket.UpdateAsync(id, b => b.Remove(b.Lines.First(l => l.Product == "pear")));

        await using var context = new RaskDbContext(
            new DbContextOptionsBuilder<RaskDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

        var rows = await context.Set<BasketLine>().IgnoreQueryFilters().CountAsync();

        Assert.Equal(1, rows);
    }

    /// <summary>
    /// The point of the bump: the version a caller read stops being current when any part of the aggregate moves.
    /// </summary>
    [Fact]
    public async Task A_version_read_before_a_child_changed_is_refused()
    {
        await using var database = await StartDatabaseAsync();
        var id = await OpenBasketAsync(database);
        var stale = (await Basket.FindAsync(id))!.Version;

        await Basket.UpdateAsync(id, o => o.Lines.First().SetQuantity(4));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => GeneratedModelWrites.UpdateAsync<Basket>(
                id, stale, o => o.Lines.First().SetQuantity(5)));
    }

    /// <summary>A save that changes nothing about the aggregate leaves its version alone.</summary>
    [Fact]
    public async Task An_untouched_aggregate_is_not_bumped()
    {
        await using var database = await StartDatabaseAsync();
        var id = await OpenBasketAsync(database);
        var before = (await Basket.FindAsync(id))!.Version;

        // Loads the whole aggregate and saves without changing anything.
        await Basket.UpdateAsync(id, _ => { });

        Assert.Equal(before, (await Basket.FindAsync(id))!.Version);
    }

    private async Task<Guid> OpenBasketAsync(TestDatabase database)
    {
        var order = Basket.Open("ada");
        order.Add("apple", 3);
        order.Add("pear", 1);

        database.Context.Add(order);
        await database.Context.SaveChangesAsync();

        // Nothing below should see this context's tracked graph.
        database.Context.ChangeTracker.Clear();

        return order.Id;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"), _clock);
}
