using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Rask.Auth;
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
        "AspNetUsers", "AspNetRoles", "AspNetUserRoles",
    ];

    /// <summary>The demo account this sample signs in with.</summary>
    /// <remarks>
    /// A real app has nobody seed it: the first person to register becomes the administrator, and while
    /// no account exists that registration needs the one-time token from the startup log. That is right
    /// for something you deploy and wrong for something you clone, run, and expect to be able to sign
    /// into — and it is what the E2E journeys drive, so it has to be deterministic.
    /// </remarks>
    public const string DemoEmail = "ada@example.com";

    /// <inheritdoc cref="DemoEmail" />
    public const string DemoPassword = "Password1";

    public static async Task InitializeAsync(
        IDbContextFactory<AppDbContext> factory, UserManager<RaskUser> users, RoleManager<IdentityRole> roles)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(roles);

        await using var db = await factory.CreateDbContextAsync().ConfigureAwait(false);
        await db.Database.EnsureCreatedAsync().ConfigureAwait(false);

        if (await CountExpectedTablesAsync(db).ConfigureAwait(false) < ExpectedTables.Length)
        {
            await db.Database.EnsureDeletedAsync().ConfigureAwait(false);
            await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
        }

        await SeedDemoAdminAsync(users, roles).ConfigureAwait(false);

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

    /// <summary>Creates the demo administrator, once.</summary>
    private static async Task SeedDemoAdminAsync(
        UserManager<RaskUser> users, RoleManager<IdentityRole> roles)
    {
        foreach (var role in RaskRoles.All)
        {
            if (!await roles.RoleExistsAsync(role).ConfigureAwait(false))
            {
                await roles.CreateAsync(new IdentityRole(role)).ConfigureAwait(false);
            }
        }

        if (await users.FindByEmailAsync(DemoEmail).ConfigureAwait(false) is not null)
        {
            return;
        }

        var user = new RaskUser
        {
            UserName = DemoEmail,
            Email = DemoEmail,
            EmailConfirmed = true,
            CreatedUtc = DateTime.UtcNow,
        };

        var created = await users.CreateAsync(user, DemoPassword).ConfigureAwait(false);

        if (created.Succeeded)
        {
            // Admin, because this account is the one that opens /_rask.
            await users.AddToRoleAsync(user, RaskRoles.Admin).ConfigureAwait(false);
        }
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
