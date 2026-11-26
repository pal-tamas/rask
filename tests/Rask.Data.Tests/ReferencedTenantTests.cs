using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Cqrs;

namespace Rask.Data.Tests;

/// <summary>A tenant's company: every reference to one that names none says so in the company's words.</summary>
public sealed class RefCompany : Aggregate<Guid, PerTenant>
{
    private RefCompany() { }

    public string Name { get; private set; } = "";

    public static RefCompany Named(string name) => new() { Id = Guid.NewGuid(), Name = name };

    public static void Configure(EntityTypeBuilder<RefCompany> builder) => builder.WhenMissing("No such company.");
}

/// <summary>A tenant's depot, with no sentence of its own.</summary>
public sealed class RefDepot : Aggregate<Guid, PerTenant>
{
    private RefDepot() { }

    public string Name { get; private set; } = "";

    public static RefDepot Named(string name) => new() { Id = Guid.NewGuid(), Name = name };
}

/// <summary>A tenant's truck: of a company, parked at a depot, its axles each serviced at a depot.</summary>
public sealed class RefTruck : Aggregate<Guid, PerTenant>
{
    private RefTruck() { }

    public string Plate { get; private set; } = "";

    public Guid? RefCompanyId { get; private set; }

    public Guid? RefDepotId { get; private set; }

    public IReadOnlyList<RefAxle> Axles { get; private set; } = [];

    public static RefTruck Of(string plate, Guid? company = null, Guid? depot = null) =>
        new() { Id = Guid.NewGuid(), Plate = plate, RefCompanyId = company, RefDepotId = depot };

    public void Renamed(string plate) => Plate = plate;

    public void MovedTo(Guid? company) => RefCompanyId = company;

    public void ServicedAt(Guid depot) => Axles = [.. Axles, RefAxle.At(depot)];

    public static void Configure(EntityTypeBuilder<RefTruck> builder)
    {
        builder.HasOne<RefCompany>().WithMany().HasForeignKey(t => t.RefCompanyId);
        builder.HasOne<RefDepot>().WithMany().HasForeignKey(t => t.RefDepotId).WhenMissing("No such depot.");
    }
}

public sealed class RefAxle : Entity<Guid>
{
    private RefAxle() { }

    public Guid ServicedAtId { get; private set; }

    internal static RefAxle At(Guid depot) => new() { Id = Guid.NewGuid(), ServicedAtId = depot };

    public static void Configure(EntityTypeBuilder<RefAxle> builder) =>
        builder.HasOne<RefDepot>().WithMany().HasForeignKey(a => a.ServicedAtId);
}

/// <summary>A load with no tenant of its own: the tenant's whose truck carries it.</summary>
public sealed class RefLoad : Aggregate<Guid, PerTenantThrough<RefTruck>>
{
    private RefLoad() { }

    public Guid RefTruckId { get; private set; }

    public static RefLoad On(Guid truck) => new() { Id = Guid.NewGuid(), RefTruckId = truck };

    public static void Configure(EntityTypeBuilder<RefLoad> builder) =>
        builder.HasOne<RefTruck>().WithMany().HasForeignKey(l => l.RefTruckId);
}

/// <summary>
///     A save of a <see cref="PerTenant" /> row refuses a reference it sets to another tenant-scoped table that names no
///     row the tenant in flight reads — another tenant's, or none — under the reference's field.
/// </summary>
public sealed class ReferencedTenantTests : IDisposable
{
    private static readonly Guid Acme = new("6a1f0c3e-2b7d-4e9a-8c1b-0000000000a1");
    private static readonly Guid Globex = new("6a1f0c3e-2b7d-4e9a-8c1b-0000000000b2");

    private readonly InMemorySqlite _sqlite = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task A_reference_to_a_row_of_the_tenant_in_flight_saves()
    {
        await using var database = await Start();
        var (acmeCompany, _) = await Companies();

        using (Tenant.Use(Acme))
        {
            await RefTruck.Of("ABC-1", acmeCompany).Save(cancellationToken: Token);

            Assert.Equal(1, await RefTruck.Count(cancellationToken: Token));
        }
    }

    [Fact]
    public async Task A_reference_to_another_tenants_row_is_refused_under_its_field_in_the_entitys_words()
    {
        await using var database = await Start();
        var (_, globexCompany) = await Companies();

        using (Tenant.Use(Acme))
        {
            var refused = await Assert.ThrowsAsync<RaskValidationException>(
                () => RefTruck.Of("ABC-1", globexCompany).Save(cancellationToken: Token));

            Assert.Equal(["No such company."], refused.Errors["RefCompanyId"]);
            Assert.Equal(0, await RefTruck.Count(cancellationToken: Token));
        }
    }

    [Fact]
    public async Task A_reference_to_no_row_at_all_is_refused_the_same_way()
    {
        await using var database = await Start();

        using (Tenant.Use(Acme))
        {
            var refused = await Assert.ThrowsAsync<RaskValidationException>(
                () => RefTruck.Of("ABC-1", Guid.NewGuid()).Save(cancellationToken: Token));

            Assert.Equal(["No such company."], refused.Errors["RefCompanyId"]);
        }
    }

    [Fact]
    public async Task A_relationships_own_sentence_answers_for_it()
    {
        await using var database = await Start();
        var globexDepot = await Depot(Globex, "north");

        using (Tenant.Use(Acme))
        {
            var refused = await Assert.ThrowsAsync<RaskValidationException>(
                () => RefTruck.Of("ABC-1", depot: globexDepot).Save(cancellationToken: Token));

            Assert.Equal(["No such depot."], refused.Errors["RefDepotId"]);
        }
    }

    [Fact]
    public async Task A_childs_reference_is_refused_under_its_row_in_the_catalogs_words()
    {
        await using var database = await Start();
        var acmeDepot = await Depot(Acme, "south");
        var globexDepot = await Depot(Globex, "north");

        using (Tenant.Use(Acme))
        {
            var truck = RefTruck.Of("ABC-1");
            truck.ServicedAt(acmeDepot);
            truck.ServicedAt(globexDepot);

            var refused = await Assert.ThrowsAsync<RaskValidationException>(() => truck.Save(cancellationToken: Token));

            Assert.Equal(["Something chosen here does not exist."], refused.Errors["Axles[1].ServicedAtId"]);
            Assert.False(refused.Errors.ContainsKey("Axles[0].ServicedAtId"));
        }
    }

    [Fact]
    public async Task Every_reference_that_misses_is_named_at_once()
    {
        await using var database = await Start();
        var (_, globexCompany) = await Companies();
        var globexDepot = await Depot(Globex, "north");

        using (Tenant.Use(Acme))
        {
            var refused = await Assert.ThrowsAsync<RaskValidationException>(
                () => RefTruck.Of("ABC-1", globexCompany, globexDepot).Save(cancellationToken: Token));

            Assert.Equal(["RefCompanyId", "RefDepotId"], refused.Errors.Keys.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task A_reference_the_save_leaves_as_it_was_is_not_asked_again()
    {
        await using var database = await Start();
        var (_, globexCompany) = await Companies();
        var id = await Lent(database, globexCompany);

        using (Tenant.Use(Acme))
        {
            var truck = (await RefTruck.Find(id, cancellationToken: Token))!;
            truck.Renamed("ABC-2");
            await truck.Save(cancellationToken: Token);

            Assert.Equal("ABC-2", (await RefTruck.First(t => t.Id == id, Token)).Plate);
        }
    }

    [Fact]
    public async Task A_reference_changed_to_another_tenants_row_is_refused()
    {
        await using var database = await Start();
        var (acmeCompany, globexCompany) = await Companies();

        using (Tenant.Use(Acme))
        {
            var truck = RefTruck.Of("ABC-1", acmeCompany);
            await truck.Save(cancellationToken: Token);
            var found = (await RefTruck.Find(truck.Id, cancellationToken: Token))!;
            found.MovedTo(globexCompany);

            var refused = await Assert.ThrowsAsync<RaskValidationException>(() => found.Save(cancellationToken: Token));

            Assert.True(refused.Errors.ContainsKey("RefCompanyId"));
            Assert.Equal(acmeCompany, (await RefTruck.First(t => t.Id == truck.Id, Token)).RefCompanyId);
        }
    }

    [Fact]
    public async Task A_row_added_in_the_same_save_is_the_tenants_own()
    {
        await using var database = await Start();

        using (Tenant.Use(Acme))
        {
            var company = RefCompany.Named("acme");
            database.Context.Add(company);
            database.Context.Add(RefTruck.Of("ABC-1", company.Id));

            await database.Context.SaveChangesAsync(Token);

            Assert.Equal(1, await RefTruck.Count(cancellationToken: Token));
        }
    }

    [Fact]
    public async Task A_tenant_through_aggregate_under_another_tenants_parent_is_refused_under_its_field()
    {
        await using var database = await Start();
        Guid globexTruck;
        using (Tenant.Use(Globex))
        {
            var truck = RefTruck.Of("XYZ-9");
            await truck.Save(cancellationToken: Token);
            globexTruck = truck.Id;
        }

        using (Tenant.Use(Acme))
        {
            var refused = await Assert.ThrowsAsync<RaskValidationException>(
                () => RefLoad.On(globexTruck).Save(cancellationToken: Token));

            Assert.True(refused.Errors.ContainsKey("RefTruckId"));
        }
    }

    [Fact]
    public async Task Across_tenants_a_reference_to_any_tenants_row_saves()
    {
        await using var database = await Start();
        var (_, globexCompany) = await Companies();

        var id = await Lent(database, globexCompany);

        using (Tenant.Use(Acme))
        {
            Assert.Equal(globexCompany, (await RefTruck.First(t => t.Id == id, Token)).RefCompanyId);
        }
    }

    [Fact]
    public async Task An_administrator_of_no_tenant_writes_into_the_tenant_it_opened_naming_a_row_of_no_tenant()
    {
        await using var database = await Start();
        var shared = await Ownerless(database);
        Guid id;

        using (Current.UsePrincipal(Administrator()))
        using (Tenant.Use(Acme))
        {
            var truck = RefTruck.Of("ADM-1", shared);
            await truck.Save(cancellationToken: Token);
            id = truck.Id;

            Assert.Equal(1, await RefTruck.Count(cancellationToken: Token));
        }

        using (Tenant.Across())
        {
            Assert.Equal(Acme, (await RefTruck.First(t => t.Id == id, Token)).TenantId);
        }
    }

    [Fact]
    public async Task An_administrator_inside_one_tenant_still_may_not_name_another_tenants_row()
    {
        await using var database = await Start();
        var (_, globexCompany) = await Companies();

        using (Current.UsePrincipal(Administrator()))
        using (Tenant.Use(Acme))
        {
            var refused = await Assert.ThrowsAsync<RaskValidationException>(
                () => RefTruck.Of("ADM-1", globexCompany).Save(cancellationToken: Token));

            Assert.Equal(["No such company."], refused.Errors["RefCompanyId"]);
        }
    }

    // A company of no tenant, as a seeded administrator's account is: a save always stamps one, so it is taken away after.
    private static async Task<Guid> Ownerless(TestDatabase database)
    {
        var id = await Company(Globex, "shared");

        using (Tenant.Across())
        {
            await database.Context.Set<RefCompany>().Where(c => c.Id == id)
                .ExecuteUpdateAsync(set => set.SetProperty(c => EF.Property<Guid?>(c, Columns.TenantId), (Guid?)null), Token);
        }

        return id;
    }

    // Signed in with no tenant claim: an administrator, who belongs to no customer.
    private static ClaimsPrincipal Administrator() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"));

    // An Acme truck of another tenant's company, written inside Tenant.Across(): the deliberate way round.
    private static async Task<Guid> Lent(TestDatabase database, Guid company)
    {
        using (Tenant.Across())
        {
            var truck = RefTruck.Of("ABC-1", company);
            database.Context.Add(truck);
            database.Context.Entry(truck).Property(Columns.TenantId).CurrentValue = Acme;
            await database.Context.SaveChangesAsync(Token);
            database.Context.ChangeTracker.Clear();
            return truck.Id;
        }
    }

    private static async Task<(Guid Acme, Guid Globex)> Companies() =>
        (await Company(Acme, "acme"), await Company(Globex, "globex"));

    private static async Task<Guid> Company(Guid tenant, string name)
    {
        using (Tenant.Use(tenant))
        {
            var company = RefCompany.Named(name);
            await company.Save(cancellationToken: Token);
            return company.Id;
        }
    }

    private static async Task<Guid> Depot(Guid tenant, string name)
    {
        using (Tenant.Use(tenant))
        {
            var depot = RefDepot.Named(name);
            await depot.Save(cancellationToken: Token);
            return depot.Id;
        }
    }

    private Task<TestDatabase> Start() => TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString));
}
