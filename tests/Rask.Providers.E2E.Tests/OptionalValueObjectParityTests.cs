using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>Where a tank's count starts — either of which an older row may lack.</summary>
public sealed record StockOpening(DateTime? Date, decimal? Quantity);

public sealed class StockPart
{
    public int Id { get; set; }

    public StockOpening? Start { get; set; }
}

/// <summary>An optional value object whose members are all optional, held in its row's own columns.</summary>
public sealed class StockPartDbContext(DbContextOptions<StockPartDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_optional_value";

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<StockPart>(b =>
        {
            b.ToTable("StockPart", Database.IsSqlite() ? null : Schema);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.ComplexProperty(p => p.Start, start => start.Property(o => o.Quantity).HasPrecision(18, 2));
        });
}

/// <summary>
///     <c>Opening? Start</c> over <c>Opening(DateTime? Date, decimal? Quantity)</c>, which EF Core alone refuses, is held
///     by every provider Rask configures: every column empty is none, and anything else is the value as written.
/// </summary>
public abstract class OptionalValueObjectParityTests(ProviderLeg leg) : ParityTests<StockPartDbContext>(leg)
{
    private static readonly DateTime Day = new(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task An_optional_value_whose_members_are_all_optional_reads_back_as_none_or_as_written()
    {
        RequireServer();
        await using (var db = Open())
        {
            db.AddRange(
                new StockPart { Id = 1 },
                new StockPart { Id = 2, Start = new StockOpening(Day, 5m) },
                new StockPart { Id = 3, Start = new StockOpening(Day, null) },
                new StockPart { Id = 4, Start = new StockOpening(null, 5m) },
                new StockPart { Id = 5, Start = new StockOpening(null, null) });
            await db.SaveChangesAsync(Ct);
        }

        await using var read = Open();
        var starts = await read.Set<StockPart>().OrderBy(p => p.Id).Select(p => p.Start).ToListAsync(Ct);
        var none = await read.Set<StockPart>().Where(p => p.Start == null).OrderBy(p => p.Id).Select(p => p.Id).ToListAsync(Ct);

        Assert.Equal([null, new StockOpening(Day, 5m), new StockOpening(Day, null), new StockOpening(null, 5m), null], starts);
        Assert.Equal([1, 5], none);
    }

    [Fact]
    public async Task Taking_the_value_away_and_giving_one_back_is_saved()
    {
        RequireServer();
        await using (var db = Open())
        {
            db.AddRange(new StockPart { Id = 1, Start = new StockOpening(Day, 5m) }, new StockPart { Id = 2 });
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = Open())
        {
            var parts = await db.Set<StockPart>().OrderBy(p => p.Id).ToListAsync(Ct);
            parts[0].Start = null;
            parts[1].Start = new StockOpening(null, 7m);
            await db.SaveChangesAsync(Ct);
        }

        await using var read = Open();
        var starts = await read.Set<StockPart>().OrderBy(p => p.Id).Select(p => p.Start).ToListAsync(Ct);
        Assert.Equal([null, new StockOpening(null, 7m)], starts);
    }

    private protected override StockPartDbContext Open() => Leg.Open<StockPartDbContext>(o => new StockPartDbContext(o));
}

public sealed class SqliteOptionalValueObjectParityTests() : OptionalValueObjectParityTests(ProviderLeg.Sqlite("optional-value"));

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerOptionalValueObjectParityTests() : OptionalValueObjectParityTests(ProviderLeg.SqlServer("rask_e2e_optional_value"));

[Collection(PostgresCollection.Name)]
public sealed class PostgresOptionalValueObjectParityTests() : OptionalValueObjectParityTests(ProviderLeg.Postgres(StockPartDbContext.Schema));
