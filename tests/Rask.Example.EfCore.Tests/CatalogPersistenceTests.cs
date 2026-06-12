using Microsoft.EntityFrameworkCore;
using Rask.Example.EfCore.Features.Catalog.Shared;

namespace Rask.Example.EfCore.Tests;

// Integration tests against a real SQLite database file: they prove the entity configuration's
// value-object converters persist and rehydrate correctly, and — answering "does SQLite support
// decimal?" — that Money is stored as an exact INTEGER (minor units), never a lossy decimal/REAL.
public sealed class CatalogPersistenceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-efcore-test-{Guid.NewGuid():N}.db");

    private CatalogDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new CatalogDbContext(options);
    }

    [Fact]
    public async Task Product_RoundTripsThroughSqlite()
    {
        await using (var db = NewContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Products.Add(Product.Create("Mechanical keyboard", 89.95m, 12));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var product = await db.Products.SingleAsync();
            Assert.Equal("Mechanical keyboard", product.Name.Value);
            Assert.Equal(89.95m, product.Price.Amount);
            Assert.Equal(12, product.Stock.Value);
        }
    }

    [Fact]
    public async Task Price_IsStoredAsIntegerMinorUnits()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
        db.Products.Add(Product.Create("Hub", 39.50m, 1));
        await db.SaveChangesAsync();

        // The raw column holds cents as an integer — exact and sortable, with no decimal/REAL column.
        // EF's scalar SqlQueryRaw<T> requires the single result column to be aliased "Value".
        var cents = await db.Database.SqlQueryRaw<long>("SELECT Price AS Value FROM Products").SingleAsync();
        Assert.Equal(3950, cents);

        var columnType = await db.Database
            .SqlQueryRaw<string>("SELECT type AS Value FROM pragma_table_info('Products') WHERE name = 'Price'")
            .SingleAsync();
        Assert.Equal("INTEGER", columnType);
    }

    [Fact]
    public async Task Update_PersistsMutationThroughTrackedAggregate()
    {
        int id;
        await using (var db = NewContext())
        {
            await db.Database.EnsureCreatedAsync();
            var created = Product.Create("Old name", 10m, 1);
            db.Products.Add(created);
            await db.SaveChangesAsync();
            id = created.Id;
        }

        await using (var db = NewContext())
        {
            var product = await db.Products.SingleAsync(p => p.Id == id);
            product.Update("New name", 25.50m, 9);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var product = await db.Products.SingleAsync(p => p.Id == id);
            Assert.Equal("New name", product.Name.Value);
            Assert.Equal(25.50m, product.Price.Amount);
            Assert.Equal(9, product.Stock.Value);
        }
    }

    public void Dispose()
    {
        foreach (var path in new[] { _dbPath, $"{_dbPath}-shm", $"{_dbPath}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
