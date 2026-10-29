using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>A tenant's silo, and the gauges fitted to it.</summary>
public sealed class Silo : Aggregate<string>, IFormModel, IDeletable, IPerTenant
{
    private readonly List<Gauge> _gauges = [];

    private Silo() { }

    public string Name { get; private set; } = "";

    public string Region { get; private set; } = "";

    public int Capacity { get; private set; }

    public IReadOnlyList<Gauge> Gauges => _gauges;

    public static Silo Built(string id, string region, int capacity, params string[] gauges)
    {
        var silo = new Silo { Id = id, Name = id, Region = region, Capacity = capacity };
        silo._gauges.AddRange(gauges.Select(Gauge.Labelled));
        return silo;
    }
}

public sealed class Gauge : Entity<string>
{
    private Gauge() { }

    public string Label { get; private set; } = "";

    internal static Gauge Labelled(string id) => new() { Id = id, Label = id };
}

/// <summary>Grain moved between two silos, read off one gauge — another aggregate, holding nothing but ids.</summary>
public sealed class Transfer : Aggregate<string>, IFormModel, ISoftDelete
{
    private Transfer() { }

    public string FromSiloId { get; private set; } = "";

    public string ToSiloId { get; private set; } = "";

    public string? GaugeId { get; private set; }

    public int Litres { get; private set; }

    public decimal Cost { get; private set; }

    public static Transfer Moved(string id, string from, string to, int litres, string? gauge = null) =>
        new() { Id = id, FromSiloId = from, ToSiloId = to, Litres = litres, Cost = litres * 0.5m, GaugeId = gauge };
}

/// <summary>A grade every tenant shares.</summary>
public sealed class Grade : Aggregate<string>, IFormModel, IDeletable
{
    private Grade() { }

    public static Grade Named(string id) => new() { Id = id };
}

/// <summary>A tenant's reading of a shared grade.</summary>
public sealed class Reading : Aggregate<string>, IFormModel, IDeletable, IPerTenant
{
    private Reading() { }

    public string GradeId { get; private set; } = "";

    public int Value { get; private set; }

    public static Reading Of(string id, string grade, int value) => new() { Id = id, GradeId = grade, Value = value };
}

/// <summary>
/// The read face of an aggregate carries the rows of another aggregate that point at it — <c>Silo.TransfersFrom</c> for
/// <c>Transfer.FromSiloId</c> — each read through ITS OWN filters, and every query that uses one is one statement.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ReverseNavigationReadTests : IDisposable
{
    private static readonly Guid Acme = new("5a0c7e2b-1d4f-4c8a-9b3e-7f6d2a1c0e91");
    private static readonly Guid Globex = new("c3e1a9d7-2b6f-4e05-8a4c-1f9b7d3e5a20");

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-reverse-{Guid.NewGuid():N}.db");
    private readonly SqlCommandLog _log = new();

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_reverse_collection_filters_and_sorts_by_the_live_rows_that_point_here_in_one_statement()
    {
        await using var database = await StartDatabaseAsync();
        await SeedSilosAsync();
        _log.Clear();

        List<string> names;
        using (Tenant.Use(Acme))
        {
            names = await Silo
                .Where(s => !s.TransfersFrom.Any())
                .OrderBy(s => s.Capacity + s.TransfersTo.Sum(t => t.Litres))
                .Select(s => s.Name);
        }

        Assert.Equal(["s3", "s2"], names);
        Assert.Single(_log.Texts);
    }

    [Fact]
    public async Task A_reverse_collection_counts_and_sums_in_a_projection_in_one_statement()
    {
        await using var database = await StartDatabaseAsync();
        await SeedSilosAsync();
        _log.Clear();

        List<(string Name, int Out, int In, decimal Cost)> rows;
        using (Tenant.Use(Acme))
        {
            rows = await Silo
                .OrderBy(s => s.Name)
                .Select(s => ValueTuple.Create(s.Name, s.TransfersFrom.Count, s.TransfersTo.Count(), s.TransfersFrom.Sum(t => t.Cost)));
        }

        Assert.Equal([("s1", 2, 0, 17.5m), ("s2", 0, 1, 0m), ("s3", 0, 1, 0m)], rows);
        Assert.Single(_log.Texts);
    }

    [Fact]
    public async Task A_reverse_collection_filters_a_grouping_in_one_statement()
    {
        await using var database = await StartDatabaseAsync();
        await SeedSilosAsync();
        _log.Clear();

        List<string> regions;
        using (Tenant.Use(Acme))
        {
            regions = await Silo.Query((q, ct) => q
                .Where(s => s.TransfersTo.Any())
                .GroupBy(s => s.Region)
                .Select(g => new { Region = g.Key, Silos = g.Count() })
                .OrderBy(g => g.Region)
                .Select(g => g.Region + ":" + g.Silos)
                .ToListAsync(ct), TestContext.Current.CancellationToken);
        }

        Assert.Equal(["north:1", "south:1"], regions);
        Assert.Single(_log.Texts);
    }

    [Fact]
    public async Task A_child_an_id_points_at_carries_the_rows_that_point_there()
    {
        await using var database = await StartDatabaseAsync();
        await SeedSilosAsync();
        _log.Clear();

        List<string> labels;
        using (Tenant.Use(Acme))
        {
            labels = await Gauge.Where(g => g.Transfers.Any()).Select(g => g.Label);
        }

        Assert.Equal(["g1"], labels);
        Assert.Single(_log.Texts);
    }

    [Fact]
    public async Task A_reverse_collection_loads_with_include_and_without_the_soft_deleted_rows()
    {
        await using var database = await StartDatabaseAsync();
        await SeedSilosAsync();

        SiloRead? s2;
        using (Tenant.Use(Acme))
        {
            s2 = await Silo.Include(s => s.TransfersTo).First(s => s.Id == "s2", TestContext.Current.CancellationToken);
        }

        Assert.Equal(["t1"], s2!.TransfersTo.Select(t => t.Id));
    }

    [Fact]
    public async Task A_reverse_collection_of_tenant_rows_under_a_shared_aggregate_holds_only_the_tenants_own()
    {
        await using var database = await StartDatabaseAsync();
        await Grade.Named("95").Save(cancellationToken: TestContext.Current.CancellationToken);
        using (Tenant.Use(Acme))
        {
            await Reading.Of("a1", "95", 1).Save(cancellationToken: TestContext.Current.CancellationToken);
            await Reading.Of("a2", "95", 2).Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        using (Tenant.Use(Globex))
        {
            await Reading.Of("g1", "95", 100).Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        (int Count, int Sum) acme, globex;
        using (Tenant.Use(Acme))
        {
            acme = (await Grade.Select(g => ValueTuple.Create(g.Readings.Count, g.Readings.Sum(r => r.Value)))).Single();
        }

        using (Tenant.Use(Globex))
        {
            globex = (await Grade.Select(g => ValueTuple.Create(g.Readings.Count, g.Readings.Sum(r => r.Value)))).Single();
        }

        Assert.Equal((2, 3), acme);
        Assert.Equal((1, 100), globex);
    }

    [Fact]
    public async Task Another_tenants_silo_brings_none_of_its_transfers_along()
    {
        await using var database = await StartDatabaseAsync();
        await SeedSilosAsync();

        int transfers;
        using (Tenant.Use(Globex))
        {
            transfers = await Silo.Query((q, ct) => q.SelectMany(s => s.TransfersFrom).CountAsync(ct), TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, transfers);
    }

    // Acme: s1 -> s2 (30 l, through g1), s1 -> s3 (5 l), and s3 -> s2 (1000 l) soft-deleted. Globex: x1 -> x1.
    private static async Task SeedSilosAsync()
    {
        var token = TestContext.Current.CancellationToken;
        using (Tenant.Use(Acme))
        {
            await Silo.Built("s1", "north", 100, "g1", "g2").Save(cancellationToken: token);
            await Silo.Built("s2", "south", 50).Save(cancellationToken: token);
            await Silo.Built("s3", "north", 10).Save(cancellationToken: token);
        }

        using (Tenant.Use(Globex))
        {
            await Silo.Built("x1", "north", 1).Save(cancellationToken: token);
        }

        await Transfer.Moved("t1", "s1", "s2", 30, gauge: "g1").Save(cancellationToken: token);
        await Transfer.Moved("t2", "s1", "s3", 5).Save(cancellationToken: token);
        await Transfer.Moved("t3", "s3", "s2", 1000).Save(cancellationToken: token);
        await Transfer.Moved("x", "x1", "x1", 7).Save(cancellationToken: token);
        await Transfer.Delete("t3", cancellationToken: token);
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}").AddInterceptors(_log), cancellationToken: TestContext.Current.CancellationToken);
}
