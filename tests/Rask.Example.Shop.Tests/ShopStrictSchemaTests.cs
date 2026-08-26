using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rask.Example.Shop.Features.Products;
using Rask.Example.Shop.Features.Shared;
using Rask.SQLite;

namespace Rask.Example.Shop.Tests;

// STRICT tables only work if every column in the model declares one of SQLite's six allowed types. The
// app's own entities are easy to check by eye; the battery packages (Outbox, Jobs, Mail, Cache) bring
// tables nobody looks at. AppDbContext wires all of them, so creating its schema under STRICT is the
// real test that the feature is usable by a Rask app rather than only by a toy model.
public sealed class ShopStrictSchemaTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"rask-shop-strict-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task The_whole_app_schema_including_every_battery_can_be_created_STRICT()
    {
        await using (var context = NewContext())
        {
            await context.Database.EnsureCreatedAsync();
        }

        var tables = ReadTableDdl();

        // Sanity: the batteries really are in here, so a passing test cannot mean "nothing was created".
        Assert.Contains(tables.Keys, t => t.Contains("Outbox", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tables.Keys, t => t.Contains("Job", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Products", tables.Keys);
        Assert.Contains("Orders", tables.Keys);

        Assert.All(tables, table =>
            Assert.True(
                table.Value.TrimEnd().EndsWith("STRICT", StringComparison.Ordinal),
                $"Table '{table.Key}' was not created STRICT: {table.Value}"));
    }

    // The app's own money columns are decimals, which are TEXT in SQLite and so legal under STRICT —
    // and still ordered numerically by the invariant collation.
    [Fact]
    public async Task Prices_round_trip_and_order_numerically_under_STRICT()
    {
        await using (var context = NewContext())
        {
            await context.Database.EnsureCreatedAsync();
            context.Products.Add(Product.Create("cheap", 9.50m, inStock: true));
            context.Products.Add(Product.Create("dear", 100.50m, inStock: true));
            context.Products.Add(Product.Create("middling", 19.95m, inStock: true));
            await context.SaveChangesAsync();
        }

        await using var read = NewContext();
        var prices = await read.Products.OrderBy(p => p.Price).Select(p => p.Price).ToListAsync();

        Assert.Equal([9.50m, 19.95m, 100.50m], prices);
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseRaskSqlite($"Data Source={_dbPath}", strictTables: true)
            .Options);

    private Dictionary<string, string> ReadTableDdl()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name, sql FROM sqlite_master WHERE type = 'table' AND sql IS NOT NULL " +
            "AND name NOT LIKE 'sqlite_%'";
        using var reader = command.ExecuteReader();

        var tables = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            tables[reader.GetString(0)] = reader.GetString(1);
        }

        return tables;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }
}
