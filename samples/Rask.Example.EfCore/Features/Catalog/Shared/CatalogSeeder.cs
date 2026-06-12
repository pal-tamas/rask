using Microsoft.EntityFrameworkCore;

namespace Rask.Example.EfCore.Features.Catalog.Shared;

// Ensures the schema exists and seeds a few products on startup. Seeding goes through the
// aggregate's Create factory (not raw column values), so even the seed data honours the invariants.
// EnsureCreated (rather than migrations) is the simplest correct choice for a sample with no
// schema history — a real app with an evolving schema would use db.Database.MigrateAsync().
public static class CatalogSeeder
{
    public static async Task SeedAsync(IDbContextFactory<CatalogDbContext> dbContextFactory)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();

        if (await db.Products.AnyAsync())
        {
            return;
        }

        db.Products.AddRange(
            Product.Create("Mechanical keyboard", 89.00m, 12),
            Product.Create("27\" 4K monitor", 329.00m, 5),
            Product.Create("USB-C hub", 39.50m, 40));

        await db.SaveChangesAsync();
    }
}
