using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rask.Data.Tests.Excise;

namespace Rask.Data.Tests;

/// <summary>A context of its own, so the model EF caches under the rules is never another test's.</summary>
public sealed class TenantReferenceContext(DbContextOptions<TenantReferenceContext> options) : RaskDbContext(options);

/// <summary>
/// The tenant reference is the write model's — a foreign key a migration makes from a table that belongs to the app's
/// tenant. The read model, whose tenant is its read face, builds without it and reads as it did.
/// </summary>
public sealed class TenantReferencesReadModelTests : IAsyncLifetime
{
    private readonly SqliteConnection _keeper = new($"Data Source=rask-tenant-read-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
    private TestDatabase<TenantReferenceContext> _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        // GuidShop is the app's tenant here, as the generated registration of the aggregate that says ITenant names it.
        CurrentRows.Use(new CurrentRow<GuidShopRead, Guid>(
            CurrentRowKind.Tenant, typeof(GuidShop), static key => row => row.Id == key, static row => row.Id));
        await _keeper.OpenAsync(Ct);
        _database = await TestDatabase.Start<TenantReferenceContext>(o => o.UseSqlite(_keeper.ConnectionString), cancellationToken: Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        await _keeper.DisposeAsync();
        CurrentRows.Reset();
    }

    [Fact]
    public async Task A_read_face_query_and_a_write_both_run_under_tenant_references()
    {
        // Started in InitializeAsync, whose flow ends before the test runs: bound here for the model surface.
        using var bound = _database.Bind();
        _database.Context.Add(NumberShop.Open(7, "Acme"));
        await _database.Context.SaveChangesAsync(Ct);
        _database.Context.ChangeTracker.Clear();

        var shop = (await NumberShop.Find(7, Ct))!;
        shop.Rename("Acme Ltd");
        await shop.Save(cancellationToken: Ct);
        var read = await NumberShop.Where(s => s.Id == 7);

        Assert.Equal(["Acme Ltd"], read.Select(static s => s.Name));
    }

    [Fact]
    public void The_write_model_still_points_a_tenant_s_table_at_the_tenant()
    {
        var goods = _database.Context.Model.FindEntityType(typeof(TaxedGood))!;

        var reference = goods.FindAnnotation("Rask:TenantReference")?.Value as string;

        Assert.EndsWith("-> GuidShop(Id)", reference, StringComparison.Ordinal);
    }
}
