using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>A tenant-scoped table whose indexes are declared every way an index can be.</summary>
[Index(nameof(Region))]
public sealed class Carrier : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel, IPerTenant
{
    private Carrier() { }

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

        // Do not name it: unique among every tenant's rows, as written.
        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasIndex(c => c.Country);

        // The tenant alone — what a foreign key to a tenants table leaves behind.
        builder.HasIndex(c => c.TenantId);
    }
}

/// <summary>
/// An index on a tenant-scoped table is exactly what the app wrote, wherever it was declared: one that names the
/// tenant holds within a tenant, one that does not holds across every tenant. Nothing puts the tenant in front.
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

    [Theory]
    [InlineData("Code")]
    [InlineData("Country")]
    [InlineData("Region")]
    public async Task An_index_that_does_not_name_the_tenant_is_left_as_declared_wherever_it_was(string column)
    {
        await using var database = await StartDatabaseAsync();

        Assert.NotNull(IndexOver(database, column));
        Assert.Null(IndexOver(database, "TenantId", column));
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
    public async Task A_unique_index_without_the_tenant_holds_across_every_tenant()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(_acme, Carrier.Named("Acme Freight", "FRT"));

        var refused = await Assert.ThrowsAsync<DbUpdateException>(
            () => SaveAsync(_globex, Carrier.Named("Globex Freight", "FRT")));

        Assert.Contains("UNIQUE", refused.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_index_that_names_the_tenant_holds_within_it_and_not_across()
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
