using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Rask.Example.Shop.Features.Products;

namespace Rask.Example.Shop.Features.Shared;

/// <summary>
/// Creates the schema and seeds a little data at startup.
/// </summary>
/// <remarks>
/// <para>
/// A real app uses migrations — <c>rask db add Init</c> then <c>rask db update</c>, which is what the
/// scaffold's next-steps tell you to run. This sample uses <see cref="DatabaseFacade.EnsureCreatedAsync"/>
/// instead so it can be cloned and run (and E2E-tested) with no migration step, at the cost of not being
/// able to evolve the schema.
/// </para>
/// <para>
/// It has to happen <b>before</b> <c>app.Run()</c>. The jobs, outbox and mail processors are hosted
/// services, and none of them catches a missing-table exception; a faulted <c>BackgroundService</c> stops
/// the host by default, so starting against an empty database doesn't produce a friendly error — the app
/// simply exits.
/// </para>
/// </remarks>
public static class DbInitializer
{
    // Every table the app needs: the domain's own, plus one per DB-backed pillar. EnsureCreated is a no-op
    // when the file already exists, so an older database missing a newly-mapped pillar table would sail
    // through and then take the host down on the first poll. Checking the set and rebuilding keeps the
    // sample runnable across changes; a migration is what does this properly.
    private static readonly string[] ExpectedTables =
    [
        "Products", "Orders", "OutboxMessage", "Job", "RecurringJobState", "QueuedMail", "CacheEntry",
    ];

    public static async Task InitializeAsync(IDbContextFactory<AppDbContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using var db = await factory.CreateDbContextAsync().ConfigureAwait(false);
        await db.Database.EnsureCreatedAsync().ConfigureAwait(false);

        if (await CountExpectedTablesAsync(db).ConfigureAwait(false) < ExpectedTables.Length)
        {
            await db.Database.EnsureDeletedAsync().ConfigureAwait(false);
            await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
        }

        if (await db.Products.AnyAsync().ConfigureAwait(false))
        {
            return;
        }

        db.Products.AddRange(
            Product.Create("Espresso beans", 12.50m, inStock: true),
            Product.Create("Filter papers", 4.00m, inStock: true),
            Product.Create("Cold brew kit", 29.95m, inStock: false));

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static async Task<int> CountExpectedTablesAsync(AppDbContext db)
    {
        // A constant query, filtered in memory: building the IN list into the SQL would be string
        // concatenation into a command (EF1002), and there is no reason to reach for it here.
        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
            .ToListAsync()
            .ConfigureAwait(false);

        return ExpectedTables.Count(tables.Contains);
    }
}
