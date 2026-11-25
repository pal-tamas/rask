using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

/// <summary>
///     A decimal column declared with a precision keeps the digits its scale holds, rounded half away from zero, as SQL
///     Server and PostgreSQL store it — the SQL Server and PostgreSQL halves are in Rask.Providers.E2E.Tests'
///     <c>DecimalParityTests</c>.
/// </summary>
public sealed class SqliteDecimalScaleTests : IAsyncLifetime
{
    private readonly InMemorySqlite _database = new();

    public async ValueTask InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task A_decimal_with_more_places_than_its_scale_is_stored_rounded_half_away_from_zero()
    {
        await using (var db = NewContext())
        {
            db.Add(new Measured { Id = 1, Cents = 1.005m, Whole = 2.5m, Free = 1.005m, Owed = -2.675m });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = NewContext();
        var row = await read.Set<Measured>().SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1.01m, row.Cents);
        Assert.Equal(3m, row.Whole);
        Assert.Equal(1.005m, row.Free);
        Assert.Equal(-2.68m, row.Owed);
    }

    [Fact]
    public async Task A_rounded_decimal_still_sums_and_orders_in_the_database()
    {
        await using var db = NewContext();
        db.AddRange(
            new Measured { Id = 1, Cents = 10.004m, Whole = 1, Free = 0 },
            new Measured { Id = 2, Cents = 9.996m, Whole = 1, Free = 0 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sum = await db.Set<Measured>().SumAsync(m => m.Cents, TestContext.Current.CancellationToken);
        var ordered = await db.Set<Measured>().OrderBy(m => m.Cents).Select(m => m.Id).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(20.00m, sum);
        Assert.Equal([1, 2], ordered);
    }

    [Fact]
    public async Task A_decimal_with_more_whole_digits_than_its_precision_holds_fails_the_save()
    {
        await using var db = NewContext();
        db.Add(new Measured { Id = 1, Cents = 10_000_000_000_000_000m, Whole = 1, Free = 0 });

        var failed = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.IsType<ArgumentOutOfRangeException>(failed.InnerException);
    }

    [Fact]
    public async Task The_largest_decimal_its_precision_holds_is_kept()
    {
        await using (var db = NewContext())
        {
            db.Add(new Measured { Id = 1, Cents = 9_999_999_999_999_999.99m, Whole = 999_999_999m, Free = 0 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = NewContext();
        var row = await read.Set<Measured>().SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal((9_999_999_999_999_999.99m, 999_999_999m), (row.Cents, row.Whole));
    }

    [Fact]
    public void A_plain_UseSqlite_leaves_the_column_as_EF_Core_maps_it()
    {
        using var db = new ScaleContext(new DbContextOptionsBuilder<ScaleContext>().UseSqlite("Data Source=:memory:").Options);

        var cents = db.Model.FindEntityType(typeof(Measured))!.FindProperty(nameof(Measured.Cents))!;

        Assert.Null(cents.GetValueConverter());
    }

    private ScaleContext NewContext() =>
        new(new DbContextOptionsBuilder<ScaleContext>().UseRaskSqliteAt(_database.ConnectionString).Options);

    public sealed class Measured
    {
        public int Id { get; set; }

        public decimal Cents { get; set; }

        public decimal Whole { get; set; }

        public decimal Free { get; set; }

        public decimal? Owed { get; set; }
    }

    private sealed class ScaleContext(DbContextOptions<ScaleContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Measured>(b =>
            {
                b.Property(m => m.Cents).HasPrecision(18, 2);
                b.Property(m => m.Whole).HasPrecision(9);
                b.Property(m => m.Owed).HasPrecision(18, 2);
            });
    }
}
