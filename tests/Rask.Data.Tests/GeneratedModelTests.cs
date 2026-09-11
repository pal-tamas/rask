using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

// A strongly-typed id: a user-defined value standing in for a Guid, so a GadgetId cannot be passed
// where another entity's id belongs. Nothing declares it as one — Rask sees it in Model<TId>.
public readonly record struct GadgetId(Guid Value);

// Value objects. Mapped as complex types, so their properties are columns on the owning row.
// A positional record is the ordinary shape, and works wherever the value object holds only scalars.
public sealed record Money(decimal Amount, string Currency) : IValueObject;

// NOT a positional record, and the difference is EF Core's, not Rask's: a complex type is materialised
// through its constructor, and EF cannot bind a *nested* complex type to a constructor parameter — it
// says so at model build ("Cannot bind 'Cost' in Packaging(Money Cost, string Material)"). So a value
// object that contains another value object needs a parameterless constructor and writable properties —
// both may be private (RASK080), since EF Core and the generated model reach them the way they reach an
// entity's. One holding only scalars, like Money above, has no such constraint.
public sealed class Packaging : IValueObject
{
    private Packaging() { } // EF materialization, and the generated PackagingModel's writes

    public Packaging(Money cost, string material)
    {
        Cost = cost;
        Material = material;
    }

    public Money Cost { get; private set; } = new(0m, "EUR");

    public string Material { get; private set; } = "card";
}

// An entity, and nothing else: no DbContext, no DbSet property, no IEntityTypeConfiguration class, no
// registration, no interface to implement. The generator finds it and RaskDbContext maps it.
public sealed class Gadget : Model<GadgetId>
{
    private Gadget() { } // EF materialization

    public string Name { get; private set; } = "";

    public string Code { get; private set; } = "";

    public Money Price { get; private set; } = new(0m, "EUR");

    public Packaging Box { get; private set; } = new(new Money(0m, "EUR"), "card");

    public static Gadget Create(string name, string code, decimal price) => new()
    {
        Id = new GadgetId(Guid.NewGuid()),
        Name = name,
        Code = code,
        Price = new Money(price, "EUR"),
        Box = new Packaging(new Money(1m, "EUR"), "card"),
    };

    // The rules that are this entity's own, in a plain static method. No attribute, no interface.
    public static void Configure(EntityTypeBuilder<Gadget> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Property(g => g.Name).HasMaxLength(64).IsRequired();
        builder.HasIndex(g => g.Code).IsUnique();
    }
}

// A second entity with no configuration and no value objects, to pin that both are optional.
public sealed class Doodad : Model<Guid>
{
    private Doodad() { }

    public string Label { get; private set; } = "";

    public static Doodad Create(string label) => new() { Id = Guid.NewGuid(), Label = label };
}

[Collection(DataDbCollection.Name)]
public sealed class GeneratedModelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-model-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _provider;

    public GeneratedModelTests()
    {
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<RaskDbContext>();
        services.AddDbContextFactory<RaskDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));

        _provider = services.BuildServiceProvider();

        using (var db = NewContext())
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

    private RaskDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<RaskDbContext>>().CreateDbContext();

    private async Task SeedAsync(params Model[] entities)
    {
        await using var db = NewContext();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    [Fact]
    public void The_generator_contributed_a_model()
    {
        Assert.False(ModelRegistry.IsEmpty);
    }

    [Fact]
    public async Task An_entity_with_no_DbSet_and_no_context_is_still_mapped()
    {
        await SeedAsync(Doodad.Create("plain"));

        Assert.Equal(1, await Doodad.CountAsync());
        Assert.Equal("plain", (await Doodad.FirstOrDefaultAsync(d => d.Label == "plain"))!.Label);
    }

    [Fact]
    public void A_plain_static_Configure_reached_the_model()
    {
        using var db = NewContext();
        var gadget = db.Model.FindEntityType(typeof(Gadget));

        Assert.NotNull(gadget);
        Assert.Equal(64, gadget.FindProperty(nameof(Gadget.Name))!.GetMaxLength());
        Assert.Contains(gadget.GetIndexes(), i => i.IsUnique && i.Properties.Any(p => p.Name == nameof(Gadget.Code)));
    }

    [Fact]
    public async Task The_unique_index_the_entity_declared_is_enforced_by_the_database()
    {
        await SeedAsync(Gadget.Create("first", "SAME", 1m));

        // Through the generated create, so its failure reaches the caller as EF's own exception.
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            GeneratedModelWrites.CreateAsync(Gadget.Create("second", "SAME", 2m)));
    }

    [Fact]
    public void A_value_object_is_mapped_as_a_complex_type_not_a_table()
    {
        using var db = NewContext();
        var gadget = db.Model.FindEntityType(typeof(Gadget))!;

        var price = gadget.FindComplexProperty(nameof(Gadget.Price));
        Assert.NotNull(price);
        Assert.NotNull(price.ComplexType.FindProperty(nameof(Money.Amount)));
        Assert.NotNull(price.ComplexType.FindProperty(nameof(Money.Currency)));

        // A complex type is part of the row, so it is not an entity type of its own — which is the
        // difference from an owned entity, and the reason two entities can share Money.
        Assert.Null(db.Model.FindEntityType(typeof(Money)));
    }

    [Fact]
    public void A_nested_value_object_is_mapped_all_the_way_down()
    {
        using var db = NewContext();
        var gadget = db.Model.FindEntityType(typeof(Gadget))!;

        var box = gadget.FindComplexProperty(nameof(Gadget.Box));
        Assert.NotNull(box);

        var cost = box.ComplexType.FindComplexProperty(nameof(Packaging.Cost));
        Assert.NotNull(cost);
        Assert.NotNull(cost.ComplexType.FindProperty(nameof(Money.Amount)));
    }

    [Fact]
    public async Task A_value_object_round_trips_through_the_database()
    {
        await SeedAsync(Gadget.Create("priced", "P1", 19.99m));

        var gadget = await Gadget.FirstOrDefaultAsync(g => g.Code == "P1");

        Assert.NotNull(gadget);
        Assert.Equal(new Money(19.99m, "EUR"), gadget.Price);
        Assert.Equal("card", gadget.Box.Material);
        Assert.Equal(1m, gadget.Box.Cost.Amount);
    }

    [Fact]
    public void A_strongly_typed_id_is_stored_as_its_underlying_value()
    {
        using var db = NewContext();
        var id = db.Model.FindEntityType(typeof(Gadget))!.FindProperty(nameof(Gadget.Id))!;

        var converter = id.GetValueConverter();
        Assert.NotNull(converter);
        Assert.Equal(typeof(GadgetId), converter.ModelClrType);
        Assert.Equal(typeof(Guid), converter.ProviderClrType);
    }

    [Fact]
    public async Task A_strongly_typed_id_round_trips_and_can_be_queried_on()
    {
        var gadget = Gadget.Create("typed", "T1", 5m);
        await SeedAsync(gadget);
        var id = gadget.Id;

        // Both routes: through the key, and through a predicate comparing the id type itself.
        Assert.NotNull(await Gadget.FindAsync(id));
        Assert.Equal("typed", (await Gadget.FirstOrDefaultAsync(g => g.Id == id))!.Name);
    }

    [Fact]
    public void Rask_conventions_still_applied_to_the_generated_model()
    {
        using var db = NewContext();
        var widget = db.Model.FindEntityType(typeof(Widget));

        // Widget is ISoftDeletable + IVersioned, so ApplyRaskConventions must have run over the model
        // ModelRegistry built — the conventions are not tied to a hand-written OnModelCreating.
        Assert.NotNull(widget);
        Assert.NotEmpty(widget.GetDeclaredQueryFilters());
        Assert.True(widget.FindProperty(nameof(Widget.Version))!.IsConcurrencyToken);
    }
}
