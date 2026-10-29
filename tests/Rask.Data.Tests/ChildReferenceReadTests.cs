using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>A pump and its nozzles: each nozzle draws from one tank.</summary>
public sealed class Dispenser : Aggregate<Guid>, IFormModel, IDeletable
{
    private readonly List<Nozzle> _nozzles = [];

    private Dispenser() { }

    public IReadOnlyCollection<Nozzle> Nozzles => _nozzles;

    public static Dispenser Installed() => new() { Id = Guid.CreateVersion7() };

    public Nozzle Fit(int tankId)
    {
        var nozzle = Nozzle.On(tankId);
        _nozzles.Add(nozzle);
        return nozzle;
    }
}

public sealed class Nozzle : Entity<Guid>
{
    private Nozzle() { }

    public int TankId { get; private set; }

    internal static Nozzle On(int tankId) => new() { Id = Guid.CreateVersion7(), TankId = tankId };
}

/// <summary>Another aggregate that names a nozzle — a child of the dispenser — by its id, and nothing more.</summary>
public sealed class Refill : Aggregate<Guid>, IFormModel, IDeletable
{
    private Refill() { }

    public Guid NozzleId { get; private set; }

    public decimal Litres { get; private set; }

    public static Refill Through(Guid nozzle, decimal litres) =>
        new() { Id = Guid.CreateVersion7(), NozzleId = nozzle, Litres = litres };
}

/// <summary>
/// The read side joins by id to another aggregate's child: a refill's <c>NozzleId</c> reaches the nozzle under its
/// dispenser, so a tank's refills are one query — while the refill itself still holds nothing but the id.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ChildReferenceReadTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-child-ref-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_read_filters_by_a_column_of_another_aggregates_child_in_one_query()
    {
        await using var database = await StartDatabaseAsync();
        var dispenser = Dispenser.Installed();
        var seven = dispenser.Fit(tankId: 7);
        var eight = dispenser.Fit(tankId: 8);
        await dispenser.Save(cancellationToken: TestContext.Current.CancellationToken);
        await Refill.Through(seven.Id, 10m).Save(cancellationToken: TestContext.Current.CancellationToken);
        await Refill.Through(seven.Id, 20m).Save(cancellationToken: TestContext.Current.CancellationToken);
        await Refill.Through(eight.Id, 40m).Save(cancellationToken: TestContext.Current.CancellationToken);

        var fromSeven = await Refill.Where(r => r.Nozzle.TankId == 7);

        Assert.Equal([10m, 20m], fromSeven.Select(r => r.Litres).Order());
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
