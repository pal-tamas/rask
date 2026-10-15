using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Providers.E2E.Tests;

/// <summary>A tenant's driver: a text key, the tenant a <see cref="Guid" /> kept as text (#1251).</summary>
public sealed class Driver : Aggregate<string>
{
    private readonly List<Chip> _chips = [];

    private Driver() { }

    public const Timestamps Stamps = Timestamps.None;

    public const Concurrency Checks = Concurrency.None;

    public const Tenancy Scope = Tenancy.PerTenant;

    public Guid? TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public IReadOnlyList<Chip> Chips => _chips;

    public static Driver Named(string key, string name, params string[] chips)
    {
        var driver = new Driver { Id = key, Name = name };
        driver._chips.AddRange(chips.Select(Chip.Coded));
        return driver;
    }

    public void TakeBack(string chip) => _chips.RemoveAll(c => c.Code == chip);
}

/// <summary>The other parent of the same chips.</summary>
public sealed class Vehicle : Aggregate<string>
{
    private readonly List<Chip> _chips = [];

    private Vehicle() { }

    public const Timestamps Stamps = Timestamps.None;

    public const Concurrency Checks = Concurrency.None;

    public const Tenancy Scope = Tenancy.PerTenant;

    public Guid? TenantId { get; private set; }

    public string Plate { get; private set; } = "";

    public IReadOnlyList<Chip> Chips => _chips;

    public static Vehicle Plated(string key, string plate, params string[] chips)
    {
        var vehicle = new Vehicle { Id = key, Plate = plate };
        vehicle._chips.AddRange(chips.Select(Chip.Coded));
        return vehicle;
    }
}

/// <summary>One table under two roots, with no tenant column: a driver's or a vehicle's.</summary>
public sealed class Chip : Entity<string>
{
    private Chip() { }

    public const Timestamps Stamps = Timestamps.None;

    public string Code { get; private set; } = "";

    public string? DriverId { get; private set; }

    public string? VehicleId { get; private set; }

    internal static Chip Coded(string code) => new() { Id = Guid.NewGuid().ToString("N"), Code = code };

    internal static Chip Under(string driver, string code) =>
        new() { Id = Guid.NewGuid().ToString("N"), Code = code, DriverId = driver };
}

/// <summary>A context of the app's own, wired by hand the way TA's is, over a schema it adopts.</summary>
public sealed class ChipDbContext(DbContextOptions<ChipDbContext> options) : DbContext(options), ITenantScoped
{
    public const string Schema = "through_parent";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Driver>(b =>
        {
            b.ToTable("Driver", Schema);
            b.Property(d => d.TenantId).HasConversion<string>().HasMaxLength(64);
            b.Property(d => d.Id).HasMaxLength(64);
        });
        modelBuilder.Entity<Vehicle>(b =>
        {
            b.ToTable("Vehicle", Schema);
            b.Property(v => v.TenantId).HasConversion<string>().HasMaxLength(64);
            b.Property(v => v.Id).HasMaxLength(64);
        });
        modelBuilder.Entity<Chip>(b =>
        {
            b.ToTable("Chip", Schema);
            b.Property(c => c.Id).HasMaxLength(64);
            b.Property(c => c.DriverId).HasMaxLength(64);
            b.Property(c => c.VehicleId).HasMaxLength(64);
        });

        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>
/// A child with no tenant column, partitioned through its two parents, on SQL Server: the filter is an EXISTS on
/// each parent's key, and every tenant reads only its own chips of both kinds.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerTenancyThroughParentTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_through_parent";

    private readonly Guid _acme = Guid.NewGuid();
    private readonly Guid _globex = Guid.NewGuid();

    public async ValueTask InitializeAsync()
    {
        if (!SqlServer.Available)
        {
            return;
        }

        await using var app = App();
        await using var db = await OpenAsync(app);
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (SqlServer.Available)
        {
            await using var app = App();
            await using var db = await OpenAsync(app);
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public void The_filter_is_an_exists_on_each_parents_key_and_tenant()
    {
        using var db = new ChipDbContext(new DbContextOptionsBuilder<ChipDbContext>()
            .UseSqlServer("Server=(local);Database=rask_model_only;Trusted_Connection=True").Options);

        string sql;
        using (Tenant.Use(_acme))
        {
            sql = db.Set<Chip>().ToQueryString();
        }

        var where = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];
        Assert.Equal(2, where.Split("EXISTS (").Length - 1);
        Assert.Contains("[through_parent].[Driver]", where, StringComparison.Ordinal);
        Assert.Contains("[through_parent].[Vehicle]", where, StringComparison.Ordinal);
        Assert.Contains("[TenantId] = @", where, StringComparison.Ordinal);
        Assert.DoesNotContain(" = 1", where, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_tenant_reads_only_its_own_chips_of_both_kinds_and_across_reads_all()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await OpenAsync(app);
        await ThroughParentApp.SeedAsync(db, _acme, _globex);

        Assert.Equal(["A-D1", "A-V1"], await ThroughParentApp.CodesAsync(db, Tenant.Use(_acme)));
        Assert.Equal(["G-D1", "G-V1"], await ThroughParentApp.CodesAsync(db, Tenant.Use(_globex)));
        Assert.Equal(["A-D1", "A-V1", "G-D1", "G-V1"], await ThroughParentApp.CodesAsync(db, Tenant.Across()));
    }

    [Fact]
    public async Task A_chip_written_under_another_tenants_driver_is_refused_and_a_severed_one_is_deleted()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await OpenAsync(app);
        await ThroughParentApp.SeedAsync(db, _acme, _globex);

        await ThroughParentApp.RefusesSmuggledChipAsync(db, _globex);
        await ThroughParentApp.DeletesSeveredChipAsync(db, _acme);
    }

    private static Task<ChipDbContext> OpenAsync(ServiceProvider app) =>
        app.GetRequiredService<IDbContextFactory<ChipDbContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);

    private static ServiceProvider App() =>
        ThroughParentApp.Build(o => o.UseSqlServer(SqlServer.Database(DatabaseName)));
}

/// <summary>The same on PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresTenancyThroughParentTests
{
    private readonly Guid _acme = Guid.NewGuid();
    private readonly Guid _globex = Guid.NewGuid();

    [Fact]
    public void The_filter_is_an_exists_on_each_parents_key_and_tenant()
    {
        using var db = new ChipDbContext(new DbContextOptionsBuilder<ChipDbContext>()
            .UseNpgsql("Host=localhost;Database=rask_model_only").Options);

        string sql;
        using (Tenant.Use(_acme))
        {
            sql = db.Set<Chip>().ToQueryString();
        }

        var where = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];
        Assert.Equal(2, where.Split("EXISTS (").Length - 1);
        Assert.Contains("through_parent.\"Driver\"", where, StringComparison.Ordinal);
        Assert.Contains("through_parent.\"Vehicle\"", where, StringComparison.Ordinal);
        Assert.Contains("\"TenantId\" = @", where, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_tenant_reads_only_its_own_chips_of_both_kinds_and_across_reads_all()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var app = ThroughParentApp.Build(o => o.UseRaskPostgresAt(Postgres.Required));
        await using var db = await ResetAsync(app);
        await ThroughParentApp.SeedAsync(db, _acme, _globex);

        Assert.Equal(["A-D1", "A-V1"], await ThroughParentApp.CodesAsync(db, Tenant.Use(_acme)));
        Assert.Equal(["G-D1", "G-V1"], await ThroughParentApp.CodesAsync(db, Tenant.Use(_globex)));
        Assert.Equal(["A-D1", "A-V1", "G-D1", "G-V1"], await ThroughParentApp.CodesAsync(db, Tenant.Across()));
    }

    [Fact]
    public async Task A_chip_written_under_another_tenants_driver_is_refused_and_a_severed_one_is_deleted()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var app = ThroughParentApp.Build(o => o.UseRaskPostgresAt(Postgres.Required));
        await using var db = await ResetAsync(app);
        await ThroughParentApp.SeedAsync(db, _acme, _globex);

        await ThroughParentApp.RefusesSmuggledChipAsync(db, _globex);
        await ThroughParentApp.DeletesSeveredChipAsync(db, _acme);
    }

    private static async Task<ChipDbContext> ResetAsync(ServiceProvider app)
    {
        var db = await app.GetRequiredService<IDbContextFactory<ChipDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        await Postgres.ResetSchemaAsync(db, ChipDbContext.Schema);
        return db;
    }
}

/// <summary>Wired the way an application wires it, and the steps both providers take.</summary>
internal static class ThroughParentApp
{
    internal static ServiceProvider Build(Action<DbContextOptionsBuilder> provider)
    {
        var services = new ServiceCollection();
        services.AddRaskData();
        services.AddDbContextFactory<ChipDbContext>((sp, o) =>
        {
            provider(o);
            o.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
        });
        return services.BuildServiceProvider();
    }

    internal static async Task SeedAsync(ChipDbContext db, Guid acme, Guid globex)
    {
        await SaveAsync(db, acme, Driver.Named("a-d", "Ada", "A-D1"), Vehicle.Plated("a-v", "AAA-111", "A-V1"));
        await SaveAsync(db, globex, Driver.Named("g-d", "Gus", "G-D1"), Vehicle.Plated("g-v", "GGG-999", "G-V1"));
    }

    internal static async Task<List<string>> CodesAsync(ChipDbContext db, IDisposable scope)
    {
        using (scope)
        {
            return await db.Set<Chip>().AsNoTracking().OrderBy(c => c.Code).Select(c => c.Code)
                .ToListAsync(TestContext.Current.CancellationToken);
        }
    }

    internal static async Task RefusesSmuggledChipAsync(ChipDbContext db, Guid tenant)
    {
        using (Tenant.Use(tenant))
        {
            db.Add(Chip.Under("a-d", "SMUGGLED"));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => db.SaveChangesAsync(TestContext.Current.CancellationToken));
            db.ChangeTracker.Clear();
        }

        using (Tenant.Across())
        {
            Assert.False(await db.Set<Chip>().AnyAsync(c => c.Code == "SMUGGLED", TestContext.Current.CancellationToken));
        }
    }

    internal static async Task DeletesSeveredChipAsync(ChipDbContext db, Guid tenant)
    {
        using (Tenant.Use(tenant))
        {
            var driver = await db.Set<Driver>().Include(d => d.Chips)
                .SingleAsync(d => d.Id == "a-d", TestContext.Current.CancellationToken);
            driver.TakeBack("A-D1");
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.ChangeTracker.Clear();
        }

        using (Tenant.Across())
        {
            Assert.False(await db.Set<Chip>().AnyAsync(c => c.Code == "A-D1", TestContext.Current.CancellationToken));
        }
    }

    private static async Task SaveAsync(ChipDbContext db, Guid tenant, params object[] rows)
    {
        using (Tenant.Use(tenant))
        {
            db.AddRange(rows);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.ChangeTracker.Clear();
        }
    }
}
