using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>A group of trucks, named — what a truck list sorts by.</summary>
public sealed class TruckFleet : Aggregate<Guid, Model, SoftDelete>
{
    private TruckFleet() { }

    public string Name { get; private set; } = "";

    public static TruckFleet Named(string name) => new() { Id = Guid.CreateVersion7(), Name = name };
}

/// <summary>A truck that names its fleet by a bare id, so the fleet's name is a correlated lookup.</summary>
public sealed class Truck : Aggregate<Guid, Model, Deletable>
{
    private Truck() { }

    public string Plate { get; private set; } = "";

    public Guid? FleetGroupId { get; private set; }

    public static Truck In(TruckFleet? fleet, string plate) =>
        new() { Id = Guid.CreateVersion7(), Plate = plate, FleetGroupId = fleet?.Id };
}

/// <summary>
/// One read face used inside another's LINQ — a correlated lookup the way an app writes it — runs as one SQL
/// query against the context the outer query opened, and never takes the process down when EF cannot translate it.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class NestedReadFaceQueryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-nested-read-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_list_orders_by_a_name_looked_up_through_another_read_face()
    {
        await using var database = await StartDatabaseAsync();
        var north = TruckFleet.Named("north");
        var east = TruckFleet.Named("east");
        await north.Save(cancellationToken: TestContext.Current.CancellationToken);
        await east.Save(cancellationToken: TestContext.Current.CancellationToken);
        await Truck.In(north, "N-1").Save(cancellationToken: TestContext.Current.CancellationToken);
        await Truck.In(east, "E-1").Save(cancellationToken: TestContext.Current.CancellationToken);
        await Truck.In(null, "X-1").Save(cancellationToken: TestContext.Current.CancellationToken);

        var byFleet = Truck.AsQueryable()
            .OrderBy(t => TruckFleet.AsQueryable().Where(f => f.Id == t.FleetGroupId).Select(f => f.Name).FirstOrDefault())
            .Select(t => t.Plate);
        var synchronously = byFleet.ToList();
        var awaited = await byFleet.ToListAsync(TestContext.Current.CancellationToken);
        var throughTheReadFace = await Truck
            .OrderBy(t => TruckFleet.AsQueryable().Where(f => f.Id == t.FleetGroupId).Select(f => f.Name).FirstOrDefault())
            .Select(t => t.Plate);

        Assert.Equal(["X-1", "E-1", "N-1"], synchronously);
        Assert.Equal(["X-1", "E-1", "N-1"], awaited);
        Assert.Equal(["X-1", "E-1", "N-1"], throughTheReadFace);
    }

    [Fact]
    public async Task The_nested_lookup_runs_inside_the_one_statement_of_the_outer_query()
    {
        var log = new SqlCommandLog();
        await using var database = await TestDatabase.Start(
            o => o.UseSqlite($"Data Source={_dbPath}").AddInterceptors(log),
            cancellationToken: TestContext.Current.CancellationToken);
        await Truck.In(null, "X-1").Save(cancellationToken: TestContext.Current.CancellationToken);
        log.Clear();

        var plates = Truck.AsQueryable()
            .OrderBy(t => TruckFleet.AsQueryable().Where(f => f.Id == t.FleetGroupId).Select(f => f.Name).FirstOrDefault())
            .Select(t => t.Plate)
            .ToList();

        Assert.Equal(["X-1"], plates);
        var select = Assert.Single(log.Texts);
        Assert.Contains("TruckFleet", select, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_nested_read_face_keeps_its_own_composed_operators_and_the_soft_delete_filter()
    {
        await using var database = await StartDatabaseAsync();
        var kept = TruckFleet.Named("kept");
        var gone = TruckFleet.Named("gone");
        await kept.Save(cancellationToken: TestContext.Current.CancellationToken);
        await gone.Save(cancellationToken: TestContext.Current.CancellationToken);
        await Truck.In(kept, "K-1").Save(cancellationToken: TestContext.Current.CancellationToken);
        await Truck.In(gone, "G-1").Save(cancellationToken: TestContext.Current.CancellationToken);
        database.Context.Remove(await database.Context.Set<TruckFleet>().SingleAsync(f => f.Id == gone.Id, TestContext.Current.CancellationToken));
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var fleets = TruckFleet.AsQueryable();
        var keptOnly = TruckFleet.Where(f => f.Name == "kept").AsQueryable();

        var inALiveFleet = Truck.AsQueryable().Where(t => fleets.Any(f => f.Id == t.FleetGroupId)).Select(t => t.Plate).ToList();
        var inKept = Truck.AsQueryable().Where(t => keptOnly.Any(f => f.Id == t.FleetGroupId)).Select(t => t.Plate).ToList();

        Assert.Equal(["K-1"], inALiveFleet);
        Assert.Equal(["K-1"], inKept);
    }

    [Fact]
    public async Task An_untranslatable_nested_read_face_query_throws_instead_of_crashing()
    {
        await using var database = await StartDatabaseAsync();
        await Truck.In(null, "X-1").Save(cancellationToken: TestContext.Current.CancellationToken);

        var untranslatable = Truck.AsQueryable()
            .OrderBy(t => TruckFleet.AsQueryable().Where(f => IsInteresting(f.Name)).Select(f => f.Name).FirstOrDefault());
        var thrown = Assert.Throws<InvalidOperationException>(() => untranslatable.ToList());

        Assert.Contains("could not be translated", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_read_face_nested_in_a_query_of_EF_Cores_own_throws_instead_of_crashing()
    {
        await using var database = await StartDatabaseAsync();
        await Truck.In(null, "X-1").Save(cancellationToken: TestContext.Current.CancellationToken);

        // EF Core's own set, which never passes through the binder: EF meets the model query's root as it is.
        var foreign = database.Context.Set<Truck>()
            .OrderBy(t => TruckFleet.AsQueryable().Where(f => f.Id == t.FleetGroupId).Select(f => f.Name).FirstOrDefault());
        var thrown = Assert.Throws<InvalidOperationException>(() => foreign.ToList());

        Assert.Contains("ModelQuery<TruckFleetRead>", thrown.Message, StringComparison.Ordinal);
    }

    // A local method EF Core cannot translate to SQL.
    private static bool IsInteresting(string name) => name.Length > 0;

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
