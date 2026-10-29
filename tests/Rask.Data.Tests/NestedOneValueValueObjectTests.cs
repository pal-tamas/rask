using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Cqrs;

namespace Rask.Data.Tests;

/// <summary>A one-value value object that is a class, with a rule.</summary>
public sealed record GondolaCode(string Value)
{
    public const string TooShort = "A code has at least two characters.";

    public static IEnumerable<string> Validate(string value) => value.Trim().Length < 2 ? [TooShort] : [];
}

/// <summary>A one-value value object that is a struct.</summary>
public readonly record struct GondolaWeight(int Value);

/// <summary>Built through its CONSTRUCTOR — the shape EF Core could not bind while the ones inside were complex.</summary>
public sealed record GondolaLabel(GondolaCode Code, GondolaCode? Alias, GondolaWeight Weight, GondolaWeight? Tare, int Copies);

/// <summary>Built through its MEMBERS — the shape that always worked, whose columns must stay as they were.</summary>
public sealed record GondolaCrate
{
    private GondolaCrate() { }

    public GondolaCode Code { get; private set; } = new("");

    public GondolaCode? Alias { get; private set; }

    public GondolaWeight Weight { get; private set; }

    public GondolaWeight? Tare { get; private set; }

    public int Units { get; private set; }

    public static GondolaCrate Of(string code, string? alias, int weight, int? tare) =>
        new()
        {
            Code = new GondolaCode(code),
            Alias = alias is null ? null : new GondolaCode(alias),
            Weight = new GondolaWeight(weight),
            Tare = tare is { } value ? new GondolaWeight(value) : null,
            Units = 1,
        };
}

/// <summary>A constructor-built STRUCT that holds a one-value value object.</summary>
public readonly record struct GondolaSpot(GondolaCode Code, int Row);

/// <summary>
/// Holds a value object that holds one-value value objects: two levels down. Built through its members, because
/// EF Core binds no value object of SEVERAL values to a constructor parameter — only a column.
/// </summary>
public sealed record GondolaRoute
{
    private GondolaRoute() { }

    public GondolaLabel Label { get; private set; } = new(new(""), null, default, null, 0);

    public string Carrier { get; private set; } = "";

    public static GondolaRoute Of(GondolaLabel label, string carrier) => new() { Label = label, Carrier = carrier };
}

/// <summary>An aggregate holding one-value value objects one and two levels inside other value objects.</summary>
public sealed class Gondola : Aggregate<int>, IFormModel
{
    private Gondola() { }

    public GondolaLabel Label { get; private set; } = new(new(""), null, default, null, 0);

    public GondolaCrate Crate { get; private set; } = GondolaCrate.Of("", null, 0, null);

    public GondolaSpot Spot { get; private set; } = new(new(""), 0);

    public GondolaRoute Route { get; private set; } = GondolaRoute.Of(new(new(""), null, default, null, 0), "");

    public static Gondola Packed(string code, string? alias = null, int weight = 1, int? tare = null)
    {
        var label = new GondolaLabel(
            new GondolaCode(code),
            alias is null ? null : new GondolaCode(alias),
            new GondolaWeight(weight),
            tare is { } value ? new GondolaWeight(value) : null,
            2);

        return new()
        {
            Label = label,
            Crate = GondolaCrate.Of(code, alias, weight, tare),
            Spot = new GondolaSpot(new GondolaCode(code), 7),
            Route = GondolaRoute.Of(label, "DHL"),
        };
    }

    public static void Configure(EntityTypeBuilder<Gondola> builder)
    {
        builder.ToTable("Gondolas");
        builder.ComplexProperty(g => g.Label).Property(l => l.Code).HasMaxLength(16);
    }
}

/// <summary>The members-built value object as it was mapped before: every one-value type inside it a complex type.</summary>
public sealed class ComplexGondola
{
    public int Id { get; set; }

    public GondolaCrate Crate { get; set; } = GondolaCrate.Of("", null, 0, null);
}

public sealed class ComplexGondolaContext(DbContextOptions<ComplexGondolaContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ComplexGondola>(e =>
        {
            e.ToTable("Gondolas");
            e.ComplexProperty(x => x.Crate, b0 =>
            {
                b0.ComplexProperty(x => x.Alias, b1 => b1.Property(v => v.Value).HasColumnName("Crate_Alias"));
                b0.ComplexProperty(x => x.Code, b1 => b1.Property(v => v.Value).HasColumnName("Crate_Code"));
                b0.ComplexProperty<GondolaWeight>(x => x.Tare, b1 => b1.Property(v => v.Value).HasColumnName("Crate_Tare"));
                b0.ComplexProperty(x => x.Weight, b1 => b1.Property(v => v.Value).HasColumnName("Crate_Weight"));
            });
        });
}

/// <summary>
/// A value object that holds one value is that value's column wherever it is held — on the aggregate, or inside
/// another value object however that one is built — and the column is the one it always had (#1244).
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class NestedOneValueValueObjectTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-nested-vo-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_one_value_value_object_inside_a_constructor_built_one_is_saved_and_read_back()
    {
        await using var database = await StartDatabaseAsync();
        var full = await SaveAsync(Gondola.Packed("AB-1", alias: "ALT", weight: 12, tare: 3));
        var bare = await SaveAsync(Gondola.Packed("CD-2"));

        var storedFull = (await database.Load<Gondola>(full, TestContext.Current.CancellationToken))!;
        var storedBare = (await database.Load<Gondola>(bare, TestContext.Current.CancellationToken))!;

        Assert.Equal(new GondolaLabel(new("AB-1"), new("ALT"), new(12), new(3), 2), storedFull.Label);
        Assert.Equal(new GondolaSpot(new("AB-1"), 7), storedFull.Spot);
        Assert.Equal(("AB-1", "ALT", 12, 3), (storedFull.Crate.Code.Value, storedFull.Crate.Alias!.Value, storedFull.Crate.Weight.Value, storedFull.Crate.Tare!.Value.Value));
        Assert.Equal(new GondolaLabel(new("CD-2"), null, new(1), null, 2), storedBare.Label);
        Assert.Null(storedBare.Crate.Alias);
        Assert.Null(storedBare.Crate.Tare);
    }

    [Fact]
    public async Task A_nullable_one_inside_another_value_object_reads_as_nothing_on_the_read_face()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Gondola.Packed("AB-1", alias: "ALT", tare: 3));
        await SaveAsync(Gondola.Packed("CD-2"));

        var aliases = await Gondola.OrderBy(s => s.LabelCode).Select(s => s.LabelAlias);
        var tares = await Gondola.OrderBy(s => s.LabelCode).Select(s => s.CrateTare);
        var without = await Gondola.Where(s => s.LabelAlias == null && s.CrateTare == null).Select(s => s.SpotCode);

        Assert.Equal(["ALT", null], aliases);
        Assert.Equal([3, null], tares);
        Assert.Equal(["CD-2"], without);
    }

    [Fact]
    public async Task One_two_levels_deep_is_saved_read_back_and_shown_on_the_read_face()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Gondola.Packed("AB-1", alias: "ALT", weight: 12));

        var stored = (await database.Load<Gondola>(id, TestContext.Current.CancellationToken))!;
        var faces = await Gondola.Where(s => s.RouteLabelCode == "AB-1" && s.RouteLabelWeight > 10).Select(s => s.RouteLabelAlias);

        Assert.Equal((new GondolaLabel(new("AB-1"), new("ALT"), new(12), null, 2), "DHL"), (stored.Route.Label, stored.Route.Carrier));
        Assert.Equal(["ALT"], faces);
    }

    [Fact]
    public async Task Each_one_is_a_converted_scalar_column_named_after_the_path_that_reaches_it()
    {
        await using var database = await StartDatabaseAsync();

        var columns = ColumnsOf(database.Context, "Gondolas");

        Assert.Equal(
            [
                "Crate_Alias TEXT NULL", "Crate_Code TEXT NOT NULL", "Crate_Tare INTEGER NULL", "Crate_Units INTEGER NOT NULL",
                "Crate_Weight INTEGER NOT NULL", "Id INTEGER NOT NULL",
                "Label_Alias TEXT NULL", "Label_Code TEXT NOT NULL", "Label_Copies INTEGER NOT NULL", "Label_Tare INTEGER NULL",
                "Label_Weight INTEGER NOT NULL",
                "Route_Carrier TEXT NOT NULL", "Route_Label_Alias TEXT NULL", "Route_Label_Code TEXT NOT NULL",
                "Route_Label_Copies INTEGER NOT NULL", "Route_Label_Tare INTEGER NULL", "Route_Label_Weight INTEGER NOT NULL",
                "Spot_Code TEXT NOT NULL", "Spot_Row INTEGER NOT NULL",
            ],
            columns);
    }

    [Fact]
    public async Task A_facet_written_on_the_nested_property_in_Configure_lands_on_its_column()
    {
        await using var database = await StartDatabaseAsync();
        var label = database.Context.Model.FindEntityType(typeof(Gondola))!.FindComplexProperty(nameof(Gondola.Label))!.ComplexType;

        var code = label.FindProperty(nameof(GondolaLabel.Code))!;
        var alias = label.FindProperty(nameof(GondolaLabel.Alias))!;

        Assert.Equal(16, code.GetMaxLength());
        Assert.Equal(typeof(string), code.GetValueConverter()!.ProviderClrType);
        Assert.False(code.IsNullable);
        Assert.True(alias.IsNullable);
    }

    [Fact]
    public async Task The_members_built_shape_keeps_exactly_the_columns_the_complex_mapping_gave_it()
    {
        await using var database = await StartDatabaseAsync();
        using var complex = new ComplexGondolaContext(
            new DbContextOptionsBuilder<ComplexGondolaContext>().UseSqlite("Data Source=:memory:").Options);

        var now = ColumnsOf(database.Context, "Gondolas").Where(c => c.StartsWith("Crate_", StringComparison.Ordinal));
        var before = ColumnsOf(complex, "Gondolas").Where(c => c.StartsWith("Crate_", StringComparison.Ordinal));

        Assert.Equal(before, now);
    }

    [Fact]
    public async Task A_row_the_scalar_mapping_writes_is_read_whole_by_the_complex_mapping_it_replaces()
    {
        await using var database = await StartDatabaseAsync();
        var options = new DbContextOptionsBuilder<ComplexGondolaContext>().UseSqlite($"Data Source={_dbPath}").Options;
        var id = await SaveAsync(Gondola.Packed("AB-1", alias: "ALT", weight: 12, tare: 3));
        var bare = await SaveAsync(Gondola.Packed("CD-2"));

        ComplexGondola oldFull, oldBare;
        await using (var complex = new ComplexGondolaContext(options))
        {
            oldFull = await complex.Set<ComplexGondola>().SingleAsync(s => s.Id == id, TestContext.Current.CancellationToken);
            oldBare = await complex.Set<ComplexGondola>().SingleAsync(s => s.Id == bare, TestContext.Current.CancellationToken);
        }

        Assert.Equal(("AB-1", "ALT", 12, 3), (oldFull.Crate.Code.Value, oldFull.Crate.Alias!.Value, oldFull.Crate.Weight.Value, oldFull.Crate.Tare!.Value.Value));
        Assert.Null(oldBare.Crate.Alias);
        Assert.Null(oldBare.Crate.Tare);
    }

    [Fact]
    public async Task The_rule_of_a_nested_one_is_asked_by_the_write_and_refused_under_its_path()
    {
        await using var database = await StartDatabaseAsync();
        var model = Gondola.Packed("AB-1").ToModel();
        model.Label!.Code = "x";
        model.Route!.Label!.Alias = "y";

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Gondola.Create(model, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            [("Label.Code", GondolaCode.TooShort), ("Route.Label.Alias", GondolaCode.TooShort)],
            refused.Failures.Select(f => (f.Fields.Single(), f.Message)).Order());
    }

    [Fact]
    public async Task The_edit_form_carries_each_nested_value_and_an_update_writes_what_it_gives_and_clears_what_it_leaves_empty()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Gondola.Packed("AB-1", alias: "ALT", weight: 12, tare: 3));

        var model = (await Gondola.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        var carried = (model.Label!.Code, model.Label.Alias, model.Label.Weight, model.Label.Tare, model.Route!.Label!.Code, model.Spot!.Code);
        model.Label.Code = "ZZ-9";
        model.Label.Alias = "NEW";
        model.Label.Tare = null;
        model.Crate!.Tare = 8;
        await Gondola.Update(id, model, cancellationToken: TestContext.Current.CancellationToken);
        var stored = (await database.Load<Gondola>(id, TestContext.Current.CancellationToken))!;

        Assert.Equal(("AB-1", "ALT", 12, 3, "AB-1", "AB-1"), carried);
        // The nested model is the whole value object: the member it leaves empty is stored empty, not kept.
        Assert.Equal(new GondolaLabel(new("ZZ-9"), new("NEW"), new(12), null, 2), stored.Label);
        Assert.Equal(new GondolaWeight(8), stored.Crate.Tare);
    }

    private static List<string> ColumnsOf(DbContext context, string table) =>
        [.. context.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables
            .Single(t => t.Name == table).Columns
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => $"{c.Name} {c.StoreType} {(c.IsNullable ? "NULL" : "NOT NULL")}")];

    private static async Task<int> SaveAsync(Gondola gondola)
    {
        await gondola.Save(cancellationToken: TestContext.Current.CancellationToken);
        return gondola.Id;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
