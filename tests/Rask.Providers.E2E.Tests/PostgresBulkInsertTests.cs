using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>A table and columns named after SQL keywords, in mixed case — the names a hand-quoted INSERT gets wrong.</summary>
public sealed class Order
{
    public Guid Id { get; set; }

    public string Group { get; set; } = "";

    public int Select { get; set; }

    // #1063: columns whose EF mapping carries no DbType, so a parameter says what it is only through its value —
    // and a nullable one whose first value is null says nothing at all.
    public decimal Total { get; set; }

    public bool Paid { get; set; }

    public DateTime PlacedAt { get; set; }

    public string? Note { get; set; }
}

public sealed class BulkDbContext(DbContextOptions<BulkDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_e2e_bulk";

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Order>().ToTable("Order");
    }
}

/// <summary>
/// <c>BulkInsertAsync</c>'s change-tracker-free writer builds its INSERT by hand, so the spelling of
/// identifiers and parameters is its own responsibility — and a schema-qualified, keyword-named, mixed-case
/// table under a retrying strategy is where that goes wrong.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresBulkInsertTests : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        if (Postgres.Available)
        {
            await using var db = NewContext();
            await Postgres.ResetSchemaAsync(db, BulkDbContext.Schema);
        }
    }

    public async Task DisposeAsync()
    {
        if (Postgres.Available)
        {
            await using var db = NewContext();
            await Postgres.DropSchemaAsync(db, BulkDbContext.Schema);
        }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ten_thousand_rows_land_in_a_keyword_named_table(bool singleTransaction)
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);

        var orders = Enumerable.Range(0, 10_000)
            .Select(i => new Order
            {
                Id = Guid.NewGuid(),
                Group = $"g{i % 7}",
                Select = i,
                Total = i + 0.25m,
                Paid = i % 2 == 0,
                PlacedAt = DateTime.UtcNow,
                Note = i % 3 == 0 ? null : $"note {i}",
            })
            .ToList();

        await using (var db = NewContext())
        {
            var written = await db.BulkInsertAsync(orders, o =>
            {
                o.SkipChangeTracking = true;
                o.SingleTransaction = singleTransaction;
            });

            Assert.Equal(10_000, written);
        }

        await using var verify = NewContext();
        Assert.Equal(10_000, await verify.Orders.CountAsync());
        Assert.Equal(orders.Sum(o => (long)o.Select), await verify.Orders.SumAsync(o => (long)o.Select));
        Assert.Equal(orders.Sum(o => o.Total), await verify.Orders.SumAsync(o => o.Total));
        Assert.Equal(orders.Count(o => o.Note is null), await verify.Orders.CountAsync(o => o.Note == null));
    }

    private static BulkDbContext NewContext() =>
        new(new DbContextOptionsBuilder<BulkDbContext>().UseRaskPostgresAt(Postgres.Required).Options);
}
