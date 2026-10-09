using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>A tenant-scoped table whose indexes are declared every way an index can be.</summary>
[Index(nameof(Region))]
public sealed class Carrier : Aggregate<Guid>
{
    private Carrier() { }

    public const Tenancy Scope = Tenancy.PerTenant;

    public Guid? TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public string Code { get; private set; } = "";

    public string Country { get; private set; } = "";

    public string Region { get; private set; } = "";

    public static Carrier Named(string name, string code) =>
        new() { Id = Guid.NewGuid(), Name = name, Code = code, Country = "HU", Region = "EU" };

    public static void Configure(EntityTypeBuilder<Carrier> builder)
    {
        // Names the tenant itself, in the order an existing table has it: left exactly as written.
        builder.HasIndex(c => new { c.Name, c.TenantId }).IsUnique();

        // Do not name it: the tenant goes in front.
        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasIndex(c => c.Country);

        // The tenant alone — what a foreign key to a tenants table leaves behind.
        builder.HasIndex(c => c.TenantId);
    }
}

/// <summary>
/// An index on a tenant-scoped table is unique WITHIN a tenant: one that does not name the tenant gets it in
/// front, wherever it was declared, and one that already names it is left exactly as written (#1233).
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class TenantIndexTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-tenant-index-{Guid.NewGuid():N}.db");
    private readonly Guid _acme = Guid.NewGuid();
    private readonly Guid _globex = Guid.NewGuid();

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task An_index_that_names_the_tenant_is_left_exactly_as_declared()
    {
        await using var database = await StartDatabaseAsync();

        var index = IndexOver(database, "Name", "TenantId");

        Assert.NotNull(index);
        Assert.True(index.IsUnique);
        Assert.Null(IndexOver(database, "TenantId", "Name"));
    }

    [Fact]
    public async Task A_unique_index_declared_in_Configure_gets_the_tenant_in_front()
    {
        await using var database = await StartDatabaseAsync();

        var index = IndexOver(database, "TenantId", "Code");

        Assert.NotNull(index);
        Assert.True(index.IsUnique);
        Assert.Null(IndexOver(database, "Code"));
    }

    [Fact]
    public async Task A_plain_index_declared_in_Configure_gets_the_tenant_in_front_and_stays_plain()
    {
        await using var database = await StartDatabaseAsync();

        var index = IndexOver(database, "TenantId", "Country");

        Assert.NotNull(index);
        Assert.False(index.IsUnique);
        Assert.Null(IndexOver(database, "Country"));
    }

    [Fact]
    public async Task An_index_declared_before_the_conventions_gets_the_tenant_in_front()
    {
        await using var database = await StartDatabaseAsync();

        Assert.NotNull(IndexOver(database, "TenantId", "Region"));
        Assert.Null(IndexOver(database, "Region"));
    }

    [Fact]
    public async Task The_index_on_the_tenant_alone_is_untouched()
    {
        await using var database = await StartDatabaseAsync();

        var indexes = database.Context.Model.FindEntityType(typeof(Carrier))!.GetIndexes().ToList();

        Assert.NotNull(IndexOver(database, "TenantId"));
        Assert.Equal(5, indexes.Count);
    }

    [Fact]
    public async Task Two_tenants_can_hold_the_same_value_in_a_unique_column()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(_acme, Carrier.Named("Acme Freight", "FRT"));

        await SaveAsync(_globex, Carrier.Named("Globex Freight", "FRT"));

        using (Tenant.Across())
        {
            Assert.Equal(2, await Carrier.Count(c => c.Code == "FRT", TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task One_tenant_cannot_hold_the_same_value_twice()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(_acme, Carrier.Named("Acme Freight", "FRT"));

        var refused = await Assert.ThrowsAsync<DbUpdateException>(
            () => SaveAsync(_acme, Carrier.Named("Acme Haulage", "FRT")));

        Assert.Contains("UNIQUE", refused.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_index_that_names_the_tenant_still_holds_within_it_and_not_across()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(_acme, Carrier.Named("Freight", "A"));

        await SaveAsync(_globex, Carrier.Named("Freight", "B"));

        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(_acme, Carrier.Named("Freight", "C")));
    }

    private static IIndex? IndexOver(TestDatabase database, params string[] columns) =>
        database.Context.Model.FindEntityType(typeof(Carrier))!.GetIndexes()
            .SingleOrDefault(i => i.Properties.Select(p => p.Name).SequenceEqual(columns));

    private static async Task SaveAsync(Guid tenant, Carrier carrier)
    {
        using (Tenant.Use(tenant))
        {
            await carrier.Save(cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
