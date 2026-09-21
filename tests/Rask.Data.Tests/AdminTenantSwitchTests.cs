using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
/// An administrator reaches a tenant's data by switching INTO it, not by reading across every tenant at once.
/// </summary>
/// <remarks>
/// <para>
/// This needs no mechanism of its own: <c>Tenant.Across()</c> lists what there is to choose from, and
/// <c>Tenant.Use(chosen)</c> is the choice. What makes it safe is that the default is neither — an admin
/// carries no tenant claim, so a tenant-scoped read before they have chosen THROWS rather than quietly
/// showing one tenant's rows or all of them.
/// </para>
/// <para>
/// Holding the choice across renders is the application's, not the framework's: it is ordinary page state,
/// and forgetting it fails closed.
/// </para>
/// </remarks>
[Collection(DataDbCollection.Name)]
public sealed class AdminTenantSwitchTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-admin-{Guid.NewGuid():N}.db");
    private readonly Guid _acme = Guid.NewGuid();
    private readonly Guid _globex = Guid.NewGuid();

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task An_admin_lists_every_tenant_then_works_inside_the_one_they_chose()
    {
        await using var database = await StartDatabaseAsync();

        await SaveAsync(database, _acme, "ACME-1");
        await SaveAsync(database, _globex, "GLOBEX-1");

        // An admin carries no tenant, so this is where they start: nothing chosen yet.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Ledger.Read.ToListAsync());

        // Across() is the one place an admin reads every tenant at once — in a real app, to show the list of
        // tenants to choose from. That list comes from the app's OWN Tenant aggregate; Rask ships only the id.
        using (Tenant.Across())
        {
            Assert.Equal(2, await Ledger.Read.CountAsync());
        }

        // Then they work inside one, and see exactly what a user of that tenant sees — no more.
        using (Tenant.Use(_acme))
        {
            Assert.Equal(["ACME-1"], await Ledger.Read.Select(l => l.Reference).ToListAsync());
        }

        // Switching is just choosing again.
        using (Tenant.Use(_globex))
        {
            Assert.Equal(["GLOBEX-1"], await Ledger.Read.Select(l => l.Reference).ToListAsync());
        }
    }

    [Fact]
    public async Task Leaving_the_chosen_tenant_returns_the_admin_to_choosing_nothing()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(database, _acme, "ACME-1");

        using (Tenant.Use(_acme))
        {
            Assert.Single(await Ledger.Read.ToListAsync());
        }

        // Fails closed on the way out too: the scope ends, and the next read has nothing to go on again.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Ledger.Read.ToListAsync());
    }

    private async Task SaveAsync(TestDatabase database, Guid tenant, string reference)
    {
        using (Tenant.Use(tenant))
        {
            database.Context.Add(Ledger.For(reference));
            await database.Context.SaveChangesAsync();
        }
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"));
}
