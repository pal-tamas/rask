using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>A value-object shaped struct that an aggregate only COMPUTES.</summary>
public readonly record struct PermitKind(string Name, int Rank);

/// <summary>An aggregate with a computed get-only property of a record struct type: no column behind it.</summary>
public sealed class Permit : Aggregate<int>
{
    private Permit() { } // EF materialization

    public const Concurrency Checks = Concurrency.None;

    public int Level { get; private set; }

    public PermitKind Kind => Level > 1 ? new PermitKind("senior", 2) : new PermitKind("junior", 1);

    public static Permit AtLevel(int level) => new() { Level = level };
}

[Collection(DataDbCollection.Name)]
public sealed class ComputedValueObjectTests
{
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
