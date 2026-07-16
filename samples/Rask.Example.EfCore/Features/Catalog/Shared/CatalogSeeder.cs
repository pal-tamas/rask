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

        // EnsureCreated no-ops on an already-existing database, so a table mapped later (the QueuedMail mail
        // table) is missing on a DB an earlier run created — and a send would fail with "no such table". This
        // demo DB has no migration history and holds only seed data, so rebuild it when a mapped table is
        // absent rather than fail on first use. (A real app with an evolving schema uses migrations instead.)
        var mailTableExists = await db.Database
            .SqlQuery<string>($"SELECT name AS Value FROM sqlite_master WHERE type = 'table' AND name = 'QueuedMail'")
            .AnyAsync();
        if (!mailTableExists)
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }

        if (await db.Products.AnyAsync())
        {
            return;
        }

        // Enough rows that the grid slice has something to page through — a pager over three rows proves
        // nothing.
        db.Products.AddRange(
            Product.Create("Mechanical keyboard", 89.00m, 12),
            Product.Create("27\" 4K monitor", 329.00m, 5),
            Product.Create("USB-C hub", 39.50m, 40),
            Product.Create("Noise-cancelling headphones", 279.00m, 0),
            Product.Create("Ergonomic chair", 349.00m, 3),
            Product.Create("Standing desk", 599.00m, 7),
            Product.Create("Bookshelf speakers", 199.00m, 9),
            Product.Create("Trackball mouse", 69.00m, 41),
            Product.Create("Monitor arm", 119.00m, 15),
            Product.Create("Desk lamp", 39.00m, 51),
            Product.Create("Webcam", 129.00m, 22),
            Product.Create("Laptop stand", 49.00m, 33));

        await db.SaveChangesAsync();
    }
}
