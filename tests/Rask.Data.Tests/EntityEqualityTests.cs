using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
/// An entity is its identity: two loads of one row are the same entity, however many instances they are, and one
/// that has no id yet is only itself — and stays findable in a set when its insert assigns the id.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class EntityEqualityTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-equality-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Two_loads_of_one_row_are_the_same_entity()
    {
        await using var database = await StartDatabaseAsync();
        var saved = Depot.Named("north");
        await saved.Save(cancellationToken: TestContext.Current.CancellationToken);

        var first = await database.Load<Depot>(saved.Id, TestContext.Current.CancellationToken);
        var second = await database.Load<Depot>(saved.Id, TestContext.Current.CancellationToken);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first!.GetHashCode(), second!.GetHashCode());
    }

    [Fact]
    public void Two_entities_with_no_id_yet_are_different_and_each_is_itself()
    {
        var north = Depot.Named("same");
        var south = Depot.Named("same");

        var same = north == south;

        Assert.False(same);
        Assert.True(north.Equals(north));
    }

    [Fact]
    public async Task An_entity_put_in_a_set_before_its_insert_is_found_after_it()
    {
        await using var database = await StartDatabaseAsync();
        var depot = Depot.Named("east");
        var set = new HashSet<Depot> { depot };

        await depot.Save(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(depot.Id > 0);
        Assert.Contains(depot, set);
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
