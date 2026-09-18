using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

// A supplier an order points at by id — never by navigation, which is the whole border.
public sealed class Supplier : Aggregate<Guid>
{
    // These tests are about soft delete, which is opt-in now.
    public const Deletion Deletes = Deletion.Soft;

    private Supplier() { } // EF materialization

    public string Country { get; private set; } = "";

    public static Supplier In(string country) => new() { Id = Guid.NewGuid(), Country = country };
}

// The courier, to prove a SUFFIX match: PickedUpByCourierId -> Courier.
public sealed class Courier : Aggregate<Guid>
{
    // These tests are about soft delete, which is opt-in now.
    public const Deletion Deletes = Deletion.Soft;

    private Courier() { } // EF materialization

    public string Name { get; private set; } = "";

    public static Courier Named(string name) => new() { Id = Guid.NewGuid(), Name = name };
}

public sealed record Freight(decimal Amount, string Currency);

public sealed class Consignment : Aggregate<Guid>
{
    // These tests are about soft delete, which is opt-in now.
    public const Deletion Deletes = Deletion.Soft;

    private readonly List<ConsignmentLine> _lines = [];

    private Consignment() { } // EF materialization

    public Guid SupplierId { get; private set; }                 // exact match  -> Supplier
    public Guid? PickedUpByCourierId { get; private set; }        // suffix match -> Courier
    public Guid ExternalRef { get; private set; }                 // no aggregate named External

    public string Reference { get; private set; } = "";
    public Freight Total { get; private set; } = new(0m, "EUR");  // flattened to two columns

    public IReadOnlyCollection<ConsignmentLine> Lines => _lines;

    public bool IsEmpty => _lines.Count == 0;                     // computed — no column, no read member

    public static Consignment For(Guid supplierId, string reference, decimal amount) =>
        new()
        {
            Id = Guid.NewGuid(),
            SupplierId = supplierId,
            ExternalRef = Guid.NewGuid(),
            Reference = reference,
            Total = new Freight(amount, "EUR"),
        };

    public Consignment With(string product, int quantity)
    {
        _lines.Add(ConsignmentLine.Of(product, quantity));
        return this;
    }

    public void PickedUpBy(Guid courierId) => PickedUpByCourierId = courierId;
}

public sealed class ConsignmentLine : Entity<Guid>
{
    private ConsignmentLine() { } // EF materialization

    public string Product { get; private set; } = "";

    public int Quantity { get; private set; }

    internal static ConsignmentLine Of(string product, int quantity) =>
        new() { Id = Guid.NewGuid(), Product = product, Quantity = quantity };
}

[Collection(DataDbCollection.Name)]
public sealed class ReadModelTests
{
    [Fact]
    public async Task Read_face_returns_the_rows_the_write_side_saved()
    {
        await using var database = await StartAsync();
        var supplier = Supplier.In("HU");

        database.Context.Add(supplier);
        database.Context.Add(Consignment.For(supplier.Id, "C-1", 120m).With("anvil", 3));
        await database.Context.SaveChangesAsync();

        var rows = await Consignment.Read.ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal("C-1", row.Reference);
        Assert.Equal(supplier.Id, row.SupplierId);
    }

    [Fact]
    public async Task A_value_object_is_flattened_to_its_columns()
    {
        await using var database = await StartAsync();
        var supplier = Supplier.In("HU");

        database.Context.Add(supplier);
        database.Context.Add(Consignment.For(supplier.Id, "C-1", 120m));
        await database.Context.SaveChangesAsync();

        // TotalAmount and TotalCurrency, not a Money — the read face is primitives, and the filter runs in
        // the database rather than over materialised rows.
        var matched = await Consignment.Read
            .Where(c => c.TotalAmount > 100m && c.TotalCurrency == "EUR")
            .CountAsync();

        Assert.Equal(1, matched);
    }

    [Fact]
    public async Task An_id_becomes_a_navigation_that_joins_across_aggregates()
    {
        await using var database = await StartAsync();
        var hungarian = Supplier.In("HU");
        var austrian = Supplier.In("AT");

        database.Context.AddRange(hungarian, austrian);
        database.Context.Add(Consignment.For(hungarian.Id, "C-1", 10m));
        database.Context.Add(Consignment.For(austrian.Id, "C-2", 20m));
        await database.Context.SaveChangesAsync();

        // The write model holds a Guid and nothing else; the join exists only on the read side.
        var references = await Consignment.Read
            .Where(c => c.Supplier.Country == "HU")
            .Select(c => c.Reference)
            .ToListAsync();

        Assert.Equal(["C-1"], references);
    }

    [Fact]
    public async Task A_prefixed_id_matches_its_aggregate_by_suffix()
    {
        await using var database = await StartAsync();
        var supplier = Supplier.In("HU");
        var courier = Courier.Named("ada");
        var consignment = Consignment.For(supplier.Id, "C-1", 10m);
        consignment.PickedUpBy(courier.Id);

        database.Context.AddRange(supplier, courier);
        database.Context.Add(consignment);
        database.Context.Add(Consignment.For(supplier.Id, "C-2", 20m));
        await database.Context.SaveChangesAsync();

        var references = await Consignment.Read
            .Where(c => c.PickedUpByCourier!.Name == "ada")
            .Select(c => c.Reference)
            .ToListAsync();

        Assert.Equal(["C-1"], references);
    }

    [Fact]
    public async Task Children_are_queryable_from_the_root_and_on_their_own()
    {
        await using var database = await StartAsync();
        var supplier = Supplier.In("HU");

        database.Context.Add(supplier);
        database.Context.Add(Consignment.For(supplier.Id, "C-1", 10m).With("anvil", 12));
        database.Context.Add(Consignment.For(supplier.Id, "C-2", 20m).With("rope", 2));
        await database.Context.SaveChangesAsync();

        var fromRoot = await Consignment.Read
            .Where(c => c.Lines.Any(l => l.Product == "anvil"))
            .Select(c => c.Reference)
            .ToListAsync();

        // A part is queryable on its own, and carries the navigation back to its root: reads have no
        // borders, even though the child is only WRITABLE through the aggregate.
        var fromChild = await ConsignmentLine.Read
            .Where(l => l.Quantity > 10 && l.Consignment.Reference == "C-1")
            .Select(l => l.Product)
            .ToListAsync();

        Assert.Equal(["C-1"], fromRoot);
        Assert.Equal(["anvil"], fromChild);
    }

    [Fact]
    public async Task An_id_matching_no_aggregate_stays_an_ordinary_column()
    {
        await using var database = await StartAsync();
        var supplier = Supplier.In("HU");
        var consignment = Consignment.For(supplier.Id, "C-1", 10m);

        database.Context.Add(supplier);
        database.Context.Add(consignment);
        await database.Context.SaveChangesAsync();

        var found = await Consignment.Read
            .Where(c => c.ExternalRef == consignment.ExternalRef)
            .CountAsync();

        // No aggregate is named "External", so there is a Guid and no navigation. That the member exists
        // at all is the assertion; a navigation would have made this a join to nowhere.
        Assert.Equal(1, found);
    }

    [Fact]
    public async Task A_soft_deleted_root_is_hidden_from_the_read_face()
    {
        await using var database = await StartAsync();
        var supplier = Supplier.In("HU");
        var consignment = Consignment.For(supplier.Id, "C-1", 10m);

        database.Context.Add(supplier);
        database.Context.Add(consignment);
        await database.Context.SaveChangesAsync();

        database.Context.Remove(consignment);
        await database.Context.SaveChangesAsync();

        Assert.Equal(0, await Consignment.Read.CountAsync());
        Assert.Equal(
            1,
            await Consignment.Read.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Reads_come_back_untracked()
    {
        await using var database = await StartAsync();
        var supplier = Supplier.In("HU");

        database.Context.Add(supplier);
        database.Context.Add(Consignment.For(supplier.Id, "C-1", 10m));
        await database.Context.SaveChangesAsync();

        await using var read = database.OpenRead();
        var row = Assert.Single(await Consignment.Read.ToListAsync());

        Assert.Empty(read.ChangeTracker.Entries());
        Assert.NotEqual(Guid.Empty, row.Id);

        // Structural, not just true of this query: the escape hatches — QueryAsync, AsQueryable, a read
        // context an app opens itself — inherit it without having to remember AsNoTracking.
        Assert.Equal(QueryTrackingBehavior.NoTracking, read.ChangeTracker.QueryTrackingBehavior);
    }

    [Fact]
    public async Task The_read_face_lands_on_the_write_models_own_columns()
    {
        await using var database = await StartAsync();
        await using var read = database.OpenRead();

        var write = database.Context.Model.FindEntityType(typeof(Consignment))!;
        var face = read.Model.FindEntityType(typeof(ConsignmentRead))!;

        // A column name is only meaningful against a table: the bare GetColumnName() of a complex
        // property is `Amount`, and what both sides must agree on is `Total_Amount`.
        var table = StoreObjectIdentifier.Create(write, StoreObjectType.Table)!.Value;

        Assert.Equal(write.GetTableName(), face.GetTableName());
        Assert.Equal(
            write.FindComplexProperty("Total")!.ComplexType.FindProperty("Amount")!.GetColumnName(table),
            face.FindProperty("TotalAmount")!.GetColumnName(table));
        Assert.Equal("Total_Amount", face.FindProperty("TotalAmount")!.GetColumnName(table));
    }

    // A computed property has no column on the write side, so it is not a member of the read face either.
    [Fact]
    public async Task A_computed_property_is_not_on_the_read_face()
    {
        await using var database = await StartAsync();

        Assert.Null(typeof(ConsignmentRead).GetProperty(nameof(Consignment.IsEmpty)));
        Assert.NotNull(typeof(ConsignmentRead).GetProperty(nameof(Consignment.Reference)));
        await Task.CompletedTask;
    }

    // The path is computed ONCE, outside the callback: the callback runs for every context the fixture
    // opens, so an interpolated GetRandomFileName() inside it would hand each one its own empty database.
    private static Task<TestDatabase> StartAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        return TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={path}"));
    }
}
