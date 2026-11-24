using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Providers.E2E.Tests;

/// <summary>A month a ledger's slips are filed into: once closed, they stay.</summary>
public sealed class SlipMonth : Aggregate<int>
{
    private SlipMonth() { } // EF materialization

    public string Name { get; private set; } = "";

    public bool Closed { get; private set; }

    public static SlipMonth Open(string name) => new() { Name = name };
}

/// <summary>A slip with a label of its own, filed into a month: what the writes over many rows are held to.</summary>
public sealed class Slip : Aggregate<int, Timestamps, Versioned, Deletable>
{
    public const string LabelTaken = "That label is taken.";

    private Slip() { } // EF materialization

    public int Month { get; private set; }

    public string Label { get; private set; } = "";

    public int Litres { get; private set; }

    public static Slip Into(int month, string label, int litres) => new() { Month = month, Label = label, Litres = litres };

    public void Pour(int litres) => Litres = litres;

    public static void Configure(EntityTypeBuilder<Slip> builder)
    {
        builder.HasIndex(s => s.Label).IsUnique(LabelTaken);
        builder.LockedBy(s => s.Month, (SlipMonth m) => m.Closed, m => $"{m.Name} is closed.");
    }
}

public sealed class SlipDbContext(DbContextOptions<SlipDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_bulk_writes";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Database.IsSqlite() ? null : Schema);
        modelBuilder.Entity<SlipMonth>();
        modelBuilder.Entity<Slip>(Slip.Configure);
        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>
///     <c>UpdateWhere</c>, <c>DeleteWhere</c>, <c>UpdateEach</c> and <c>Import</c> the same on every provider: the set
///     statement keeps its stamps and asks its locks in the database's own SQL, and an import that meets a unique index
///     sets the row aside and saves the rest — on PostgreSQL too, whose transaction ends at the first error.
/// </summary>
public abstract class BulkWritesParityTests(ProviderLeg leg) : ParityTests<SlipDbContext>(leg)
{
    [Fact]
    public async Task DeleteWhere_deletes_an_open_months_slips_and_refuses_a_closed_ones()
    {
        RequireServer();
        var open = await MonthAsync("April", close: false);
        var closed = await MonthAsync("March", close: false);
        await SlipsAsync(Slip.Into(open, "a1", 1), Slip.Into(open, "a2", 2), Slip.Into(closed, "m1", 3));
        await CloseAsync(closed);

        var (deleted, refused) = await OnTheSurface(async () =>
            (await Slip.DeleteWhere(s => s.Month == open, Ct),
             await Assert.ThrowsAsync<RaskValidationException>(() => Slip.DeleteWhere(s => s.Month == closed, Ct))));

        await using var db = Open();
        Assert.Equal(2, deleted);
        Assert.Equal("March is closed.", Assert.Single(refused.Failures).Message);
        Assert.Equal(["m1"], await db.Set<Slip>().Select(s => s.Label).ToListAsync(Ct));
    }

    [Fact]
    public async Task UpdateWhere_bumps_the_version_and_refuses_a_move_into_a_closed_month()
    {
        RequireServer();
        var open = await MonthAsync("April", close: false);
        var closed = await MonthAsync("March", close: true);
        await SlipsAsync(Slip.Into(open, "a1", 1));

        var refused = await OnTheSurface(async () =>
        {
            await Slip.UpdateWhere(s => s.Label == "a1", s => s.SetProperty(x => x.Litres, 9), Ct);
            return await Assert.ThrowsAsync<RaskValidationException>(() =>
                Slip.UpdateWhere(s => s.Label == "a1", s => s.SetProperty(x => x.Month, closed), Ct));
        });

        await using var db = Open();
        var stored = await db.Set<Slip>().SingleAsync(Ct);
        Assert.Equal((9, 1, open), (stored.Litres, stored.Version, stored.Month));
        Assert.Equal(["Month"], Assert.Single(refused.Failures).Fields);
    }

    [Fact]
    public async Task UpdateEach_saves_each_slip_through_its_method()
    {
        RequireServer();
        var open = await MonthAsync("April", close: false);
        await SlipsAsync(Slip.Into(open, "a1", 1), Slip.Into(open, "a2", 2));

        var updated = await OnTheSurface(() => Slip.UpdateEach(s => s.Month == open, s => s.Pour(s.Litres * 10), batchSize: 1, Ct));

        await using var db = Open();
        Assert.Equal(2, updated);
        Assert.Equal([10, 20], await db.Set<Slip>().OrderBy(s => s.Label).Select(s => s.Litres).ToListAsync(Ct));
    }

    [Fact]
    public async Task Import_sets_aside_a_row_the_unique_index_refuses_and_saves_the_rest_of_its_batch()
    {
        RequireServer();
        var open = await MonthAsync("April", close: false);
        await SlipsAsync(Slip.Into(open, "a1", 1));
        (string Label, int Litres)[] rows = [("b1", 1), ("a1", 2), ("b2", 3)];

        var result = await OnTheSurface(() => Slip.Import(
            rows, row => Slip.Into(open, row.Label, row.Litres), static o => o.OnInvalid = ImportFailure.Collect, Ct));

        await using var db = Open();
        Assert.Equal((2, 1), (result.Inserted, Assert.Single(result.Failures).Index));
        Assert.Equal([Slip.LabelTaken], result.Failures[0].Refusal.Errors["Label"]);
        Assert.Equal(["a1", "b1", "b2"], await db.Set<Slip>().OrderBy(s => s.Label).Select(s => s.Label).ToListAsync(Ct));
    }

    private protected override SlipDbContext Open() => Leg.Open<SlipDbContext>(o => new SlipDbContext(o));

    // The writes on the type reach the database through Db's surface, pointed at this leg for the call.
    private async Task<T> OnTheSurface<T>(Func<Task<T>> work)
    {
        Db.Configure(Open);
        try
        {
            return await work();
        }
        finally
        {
            Db.Reset();
        }
    }

    private async Task<int> MonthAsync(string name, bool close)
    {
        await using var db = Open();
        var month = SlipMonth.Open(name);
        db.Add(month);
        await db.SaveChangesAsync(Ct);
        if (close)
        {
            await CloseAsync(month.Id);
        }

        return month.Id;
    }

    private async Task CloseAsync(int month)
    {
        await using var db = Open();
        await db.Set<SlipMonth>().Where(m => m.Id == month).ExecuteUpdateAsync(s => s.SetProperty(m => m.Closed, true), Ct);
    }

    private async Task SlipsAsync(params Slip[] slips)
    {
        await using var db = Open();
        db.AddRange(slips);
        await db.SaveChangesAsync(Ct);
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class SqliteBulkWritesParityTests() : BulkWritesParityTests(ProviderLeg.Sqlite("bulk-writes"));

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerBulkWritesParityTests() : BulkWritesParityTests(ProviderLeg.SqlServer("rask_e2e_bulk_writes"));

[Collection(PostgresCollection.Name)]
public sealed class PostgresBulkWritesParityTests() : BulkWritesParityTests(ProviderLeg.Postgres(SlipDbContext.Schema));
