using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>A value-object shaped struct that an aggregate only COMPUTES.</summary>
public readonly record struct PermitKind(string Name, int Rank);

/// <summary>An aggregate with a computed get-only property of a record struct type: no column behind it.</summary>
public sealed class Permit : Aggregate<int, Timestamps, Model, Deletable>
{
    private Permit() { } // EF materialization

    public int Level { get; private set; }

    public PermitKind Kind => Level > 1 ? new PermitKind("senior", 2) : new PermitKind("junior", 1);

    public static Permit AtLevel(int level) => new() { Level = level };
}

/// <summary>An aggregate whose field-backed lists sit beside members computed from them.</summary>
public sealed class Hamper : Aggregate<Guid, Model, Deletable>
{
    private readonly List<HamperItem> _items = [];
    private readonly List<string> _labels = [];

    private Hamper() { } // EF materialization

    public string Owner { get; private set; } = "";

    public IReadOnlyList<HamperItem> Items => _items;

    public IEnumerable<HamperItem> HeavyItems => _items.Where(i => i.Grams >= 500);

    public IReadOnlyList<string> Labels => _labels;

    public IReadOnlyList<string> SortedLabels => [.. _labels.Order(StringComparer.Ordinal)];

    public int TotalGrams => _items.Sum(i => i.Grams);

    public static Hamper Pack(string owner, params (string Name, int Grams)[] items)
    {
        var hamper = new Hamper { Id = Guid.CreateVersion7(), Owner = owner };
        hamper._items.AddRange(items.Select(static i => HamperItem.Of(i.Name, i.Grams)));
        hamper._labels.AddRange(["fragile", "chilled"]);
        return hamper;
    }
}

public sealed class HamperItem : Entity<Guid>
{
    private HamperItem() { } // EF materialization

    public string Name { get; private set; } = "";

    public int Grams { get; private set; }

    internal static HamperItem Of(string name, int grams) => new() { Id = Guid.CreateVersion7(), Name = name, Grams = grams };
}

[Collection(DataDbCollection.Name)]
public sealed class ComputedValueObjectTests
{
    [Fact]
    public async Task A_member_computed_from_a_field_backed_list_is_neither_mapped_nor_on_the_form_model()
    {
        await using var database = await StartAsync();
        var hamper = Hamper.Pack("ada", ("cheese", 700), ("jam", 300));
        await hamper.Save(cancellationToken: TestContext.Current.CancellationToken);

        var model = await Hamper.Model(hamper.Id, cancellationToken: TestContext.Current.CancellationToken);

        var mapped = database.Context.Model.FindEntityType(typeof(Hamper))!;
        Assert.Null(mapped.FindNavigation(nameof(Hamper.HeavyItems)));
        Assert.Null(mapped.FindProperty(nameof(Hamper.SortedLabels)));
        Assert.Null(typeof(HamperModel).GetProperty(nameof(Hamper.HeavyItems)));
        Assert.Null(typeof(HamperModel).GetProperty(nameof(Hamper.SortedLabels)));
        Assert.Null(typeof(HamperModel).GetProperty(nameof(Hamper.TotalGrams)));
        Assert.Equal(["cheese", "jam"], model!.Items.Select(i => i.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["fragile", "chilled"], model.Labels);
    }

    [Fact]
    public async Task A_computed_get_only_property_of_a_struct_type_is_not_a_column()
    {
        await using var database = await StartAsync();
        database.Context.Add(Permit.AtLevel(2));
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var permit = await Permit.Where(p => p.Level == 2).First(TestContext.Current.CancellationToken);

        var mapped = database.Context.Model.FindEntityType(typeof(Permit))!;
        Assert.Null(mapped.FindProperty(nameof(Permit.Kind)));
        Assert.Null(mapped.FindComplexProperty(nameof(Permit.Kind)));
        Assert.NotNull(permit);
        Assert.Equal("senior", (await database.Context.Set<Permit>().SingleAsync(TestContext.Current.CancellationToken)).Kind.Name);
    }

    private static Task<TestDatabase> StartAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        return TestDatabase.Start(o => o.UseSqlite($"Data Source={path}"));
    }
}
