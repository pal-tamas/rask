using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>A payment: an amount of cents and a rate of nine places, as Eszaf's litres and prices are declared.</summary>
public sealed class Payment
{
    public int Id { get; set; }

    public decimal Amount { get; set; }

    public decimal Rate { get; set; }
}

public sealed class DecimalDbContext(DbContextOptions<DecimalDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_decimals";

    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Database.IsSqlite() ? null : Schema);
        modelBuilder.Entity<Payment>(b =>
        {
            b.Property(p => p.Amount).HasPrecision(18, 2);
            b.Property(p => p.Rate).HasPrecision(18, 9);
        });
    }
}

/// <summary>
///     A decimal is exact on every provider — stored to its last declared digit, summed, compared, ordered and multiplied
///     as the number it is — and SQLite, which has no decimal type, holds to it through <c>UseRaskSqlite</c>.
/// </summary>
public abstract class DecimalParityTests(ProviderLeg leg) : ParityTests<DecimalDbContext>(leg)
{
    [Fact]
    public async Task A_decimal_comes_back_to_its_last_declared_digit()
    {
        RequireServer();
        await using (var db = Open())
        {
            db.Payments.Add(new Payment { Amount = 9_999_999_999_999_999.99m, Rate = 123_456_789.123456789m });
            await db.SaveChangesAsync(Ct);
        }

        await using var read = Open();
        var payment = await read.Payments.SingleAsync(Ct);

        Assert.Equal(9_999_999_999_999_999.99m, payment.Amount);
        Assert.Equal(123_456_789.123456789m, payment.Rate);
    }

    [Fact]
    public async Task A_sum_of_decimals_is_exact()
    {
        RequireServer();
        await using var db = Open();
        db.Payments.AddRange(Enumerable.Range(0, 10).Select(_ => new Payment { Amount = 0.10m, Rate = 0.000000001m }));
        db.Payments.Add(new Payment { Amount = 1_000_000_000_000_000.00m, Rate = 0m });
        await db.SaveChangesAsync(Ct);

        var amounts = await db.Payments.SumAsync(p => p.Amount, Ct);
        var rates = await db.Payments.SumAsync(p => p.Rate, Ct);

        Assert.Equal(1_000_000_000_000_001.00m, amounts);
        Assert.Equal(0.00000001m, rates);
    }

    [Fact]
    public async Task Decimals_compare_and_order_by_their_value()
    {
        RequireServer();
        await using var db = Open();
        db.Payments.AddRange(Of(10.50m), Of(9.75m), Of(-2.00m), Of(100.00m), Of(9.80m));
        await db.SaveChangesAsync(Ct);

        var ordered = await db.Payments.OrderBy(p => p.Amount).Select(p => p.Amount).ToListAsync(Ct);
        var above = await db.Payments.Where(p => p.Amount > 9.79m).OrderByDescending(p => p.Amount).Select(p => p.Amount).ToListAsync(Ct);
        var least = await db.Payments.MinAsync(p => p.Amount, Ct);
        var most = await db.Payments.MaxAsync(p => p.Amount, Ct);

        Assert.Equal([-2.00m, 9.75m, 9.80m, 10.50m, 100.00m], ordered);
        Assert.Equal([100.00m, 10.50m, 9.80m], above);
        Assert.Equal(-2.00m, least);
        Assert.Equal(100.00m, most);
    }

    [Fact]
    public async Task Decimal_arithmetic_in_a_query_is_exact()
    {
        RequireServer();
        await using var db = Open();
        db.Payments.Add(new Payment { Amount = 0.10m, Rate = 3.000000000m });
        db.Payments.Add(new Payment { Amount = 0.20m, Rate = 0.100000000m });
        await db.SaveChangesAsync(Ct);

        var exactly = await db.Payments.CountAsync(p => p.Amount * p.Rate == 0.30m, Ct);
        var added = await db.Payments.Where(p => p.Amount + 0.10m == 0.30m).Select(p => p.Rate).SingleAsync(Ct);

        Assert.Equal(1, exactly);
        Assert.Equal(0.1m, added);
    }

    [Fact]
    public async Task A_decimal_with_more_places_than_its_scale_is_kept_as_the_column_rounds_it()
    {
        RequireServer();
        await using (var db = Open())
        {
            db.Payments.Add(new Payment { Amount = 1.005m, Rate = 1m });
            db.Payments.Add(new Payment { Amount = -2.675m, Rate = 2m });
            db.Payments.Add(new Payment { Amount = 3.004m, Rate = 3m });
            await db.SaveChangesAsync(Ct);
        }

        await using var read = Open();
        var amounts = await read.Payments.OrderBy(p => p.Rate).Select(p => p.Amount).ToListAsync(Ct);

        Assert.Equal([1.01m, -2.68m, 3.00m], amounts);
    }

    [Fact]
    public async Task A_decimal_with_more_whole_digits_than_its_precision_holds_is_refused()
    {
        RequireServer();
        await using var db = Open();

        db.Payments.Add(new Payment { Amount = 100_000_000_000_000_000m, Rate = 1m });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        await using var read = Open();
        Assert.Equal(0, await read.Payments.CountAsync(Ct));
    }

    private protected override DecimalDbContext Open() => Leg.Open<DecimalDbContext>(o => new DecimalDbContext(o));

    private static Payment Of(decimal amount) => new() { Amount = amount, Rate = 1m };
}

public sealed class SqliteDecimalParityTests() : DecimalParityTests(ProviderLeg.Sqlite("decimals"));

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerDecimalParityTests() : DecimalParityTests(ProviderLeg.SqlServer("rask_e2e_decimals"));

[Collection(PostgresCollection.Name)]
public sealed class PostgresDecimalParityTests() : DecimalParityTests(ProviderLeg.Postgres(DecimalDbContext.Schema));
