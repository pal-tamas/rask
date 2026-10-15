using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Data.Tests;

/// <summary>
/// <see cref="Chip" /> over a table somebody else made, in a host wired by hand: its declared unique rule is asked
/// as a query before the save, through the parent — so it holds within a tenant, as the chip does (#1251).
/// </summary>
/// <remarks>
/// The table's unique index is DROPPED first, so a refusal with no inner exception is the check's.
/// </remarks>
[Collection(DataDbCollection.Name)]
public sealed class TenancyThroughParentAdoptedSchemaTests : IAsyncLifetime
{
    private static readonly Guid Acme = new("0f9d5a3c-7c1e-4d52-9a54-2b6f1f0c8e11");
    private static readonly Guid Globex = new("a1b2c3d4-0000-4000-8000-00000000beef");

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-through-parent-adopted-{Guid.NewGuid():N}.db");
    private ServiceProvider _app = null!;

    public async ValueTask InitializeAsync()
    {
        var connection = $"Data Source={_dbPath};Pooling=False";
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<DomainContext>();
        services.AddDeclaredRuleChecks();
        services.AddDbContextFactory<DomainContext>((sp, o) => o
            .UseSqlite(connection)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite(connection));

        _app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(_app);

        await using var context = await OpenAsync();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync("DROP INDEX \"Chip_code_key\";", TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        await _app.DisposeAsync();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task A_code_the_tenant_already_handed_to_a_driver_is_refused_for_its_vehicle()
    {
        await SaveAsync(Acme, Driver.Named("a-d", "Ada", "C-1"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => SaveAsync(Acme, Vehicle.Plated("a-v", "AAA-111", "C-1")));

        Assert.Equal(Chip.Taken, Assert.Single(refused.Failures).Message);
        Assert.Null(refused.InnerException);
        Assert.Equal(1, await ChipsAsync());
    }

    [Fact]
    public async Task A_code_another_tenant_holds_passes_because_the_rule_is_asked_through_the_parent()
    {
        await SaveAsync(Acme, Driver.Named("a-d", "Ada", "C-1"));

        await SaveAsync(Globex, Driver.Named("g-d", "Gus", "C-1"));

        Assert.Equal(2, await ChipsAsync());
    }

    [Fact]
    public async Task Each_tenant_reads_its_own_chips_through_the_hand_wired_host()
    {
        await SaveAsync(Acme, Driver.Named("a-d", "Ada", "A-1"));
        await SaveAsync(Globex, Vehicle.Plated("g-v", "GGG-999", "G-1"));

        using (Tenant.Use(Acme))
        {
            Assert.Equal(["A-1"], await Chip.Select(c => c.Code));
        }

        using (Tenant.Use(Globex))
        {
            Assert.Equal(["G-1"], await Chip.Select(c => c.Code));
        }
    }

    private static async Task SaveAsync(Guid tenant, Driver driver)
    {
        using (Tenant.Use(tenant))
        {
            await driver.Save(cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    private static async Task SaveAsync(Guid tenant, Vehicle vehicle)
    {
        using (Tenant.Use(tenant))
        {
            await vehicle.Save(cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    private async Task<int> ChipsAsync()
    {
        await using var context = await OpenAsync();
        return (await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM Chip")
            .ToListAsync(TestContext.Current.CancellationToken)).Single();
    }

    private Task<DomainContext> OpenAsync() =>
        _app.GetRequiredService<IDbContextFactory<DomainContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);
}
