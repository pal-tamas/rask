using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// A row that points at another row of its OWN table: SumTotalizerId -> Totalizer.
public sealed class Totalizer : Aggregate<int, Timestamps, Model, Deletable>
{
    private Totalizer() { } // EF materialization

    public string Number { get; private set; } = "";

    public int? SumTotalizerId { get; private set; }

    public static Totalizer Numbered(string number) => new() { Number = number };

    public void SumUpIn(Totalizer sum) => SumTotalizerId = sum.Id;
}

[Collection(DataDbCollection.Name)]
public sealed class SelfReferenceReadTests
{
    [Fact]
    public async Task An_id_that_points_at_its_own_table_joins_and_sorts_by_the_other_row()
    {
        await using var database = await StartAsync();
        var sum = Totalizer.Numbered("Z-SUM");
        var other = Totalizer.Numbered("A-SUM");
        database.Context.AddRange(sum, other);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var first = Totalizer.Numbered("one");
        var second = Totalizer.Numbered("two");
        first.SumUpIn(sum);
        second.SumUpIn(other);
        database.Context.AddRange(first, second);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var numbers = await Totalizer
            .Where(v => v.SumTotalizer != null)
            .OrderBy(v => v.SumTotalizer!.Number)
            .Select(v => v.Number);

        Assert.Equal(["two", "one"], numbers);
    }

    private static Task<TestDatabase> StartAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        return TestDatabase.Start(o => o.UseSqlite($"Data Source={path}"));
    }
}
