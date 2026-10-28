using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests.Excise;

/// <summary>A product category, whose name a guess from <c>ForeignTaxProductCategoryId</c> would land on.</summary>
public sealed class ProductCategory : Aggregate<int>
{
    public string Name { get; private set; } = "";

    public static void Configure(EntityTypeBuilder<ProductCategory> builder) => builder.ToTable("ProductCategories");
}

/// <summary>The category the foreign tax is really kept under.</summary>
public sealed class ForeignTaxCategory : Aggregate<int>
{
    public string Name { get; private set; } = "";

    public static void Configure(EntityTypeBuilder<ForeignTaxCategory> builder) => builder.ToTable("ForeignTaxCategories");
}

/// <summary>A tenant's good, holding one id to each category — the second one under a name that ends in the first.</summary>
public sealed class TaxedGood : Aggregate<int, PerTenant>
{
    public int? TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public int? ProductCategoryId { get; private set; }

    public int? ForeignTaxProductCategoryId { get; private set; }

    public static void Configure(EntityTypeBuilder<TaxedGood> builder)
    {
        builder.ToTable("TaxedGoods");
        builder.HasOne<ForeignTaxCategory>().WithMany().HasForeignKey(g => g.ForeignTaxProductCategoryId);
    }
}

/// <summary>A country a provider is registered in.</summary>
public sealed class Country : Aggregate<int>
{
    public static void Configure(EntityTypeBuilder<Country> builder) => builder.ToTable("Countries");
}

/// <summary>A tenant's provider, kept in a table an older app named after transaction types.</summary>
public sealed class Provider : Aggregate<int, PerTenant>
{
    public int? TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public static void Configure(EntityTypeBuilder<Provider> builder) => builder.ToTable("TransactionTypes");
}

/// <summary>A provider's VAT number in a country, keyed by the pair, the provider's part under its legacy column name.</summary>
public sealed partial class ProviderCountry : Entity<(int TransactionTypeId, Country)>
{
    public int TransactionTypeId { get; private set; }

    public string VatNumber { get; private set; } = "";

    public static void Configure(EntityTypeBuilder<ProviderCountry> builder)
    {
        builder.ToTable("TransactionTypeCountries");
        builder.HasOne<Provider>().WithMany().HasForeignKey(c => c.TransactionTypeId);
        builder.HasOne<Country>().WithMany().HasForeignKey(c => c.CountryId);
    }
}

/// <summary>The context the excise tests open.</summary>
public sealed class ExciseContext(DbContextOptions<ExciseContext> options) : RaskDbContext(options);

/// <summary>
/// A navigation goes where <c>Configure</c> says it goes: to the configured table when the id's name ends in another
/// aggregate's, and at all when the id keeps a legacy name — on a composite-keyed row under a per-tenant parent.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ConfiguredRelationshipReadTests : IAsyncLifetime
{
    private const int Acme = 7;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-configured-fk-{Guid.NewGuid():N}.db");
    private ServiceProvider _app = null!;

    public async ValueTask InitializeAsync()
    {
        var connection = $"Data Source={_dbPath};Pooling=False";
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<ExciseContext>();
        services.AddDbContextFactory<ExciseContext>((sp, o) => o
            .UseSqlite(connection)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite(connection));

        _app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(_app);

        await using var context = await _app.GetRequiredService<IDbContextFactory<ExciseContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "ProductCategories" ("Id" INTEGER NOT NULL PRIMARY KEY, "Name" TEXT NOT NULL);
            CREATE TABLE "ForeignTaxCategories" ("Id" INTEGER NOT NULL PRIMARY KEY, "Name" TEXT NOT NULL);
            CREATE TABLE "TaxedGoods" (
                "Id" INTEGER NOT NULL PRIMARY KEY, "TenantId" INTEGER NULL, "Name" TEXT NOT NULL,
                "ProductCategoryId" INTEGER NULL, "ForeignTaxProductCategoryId" INTEGER NULL);
            CREATE TABLE "Countries" ("Id" INTEGER NOT NULL PRIMARY KEY);
            CREATE TABLE "TransactionTypes" ("Id" INTEGER NOT NULL PRIMARY KEY, "TenantId" INTEGER NULL, "Name" TEXT NOT NULL);
            CREATE TABLE "TransactionTypeCountries" (
                "TransactionTypeId" INTEGER NOT NULL, "CountryId" INTEGER NOT NULL, "VatNumber" TEXT NOT NULL,
                PRIMARY KEY ("TransactionTypeId", "CountryId"));
            INSERT INTO "ProductCategories" VALUES (1, 'Fuel'), (2, 'Toll');
            INSERT INTO "ForeignTaxCategories" VALUES (1, 'Excise duty'), (2, 'Road tax');
            INSERT INTO "TaxedGoods" VALUES (10, 7, 'Diesel', 1, 2), (11, 8, 'Petrol', 2, 1);
            INSERT INTO "Countries" VALUES (19), (23);
            INSERT INTO "TransactionTypes" VALUES (1, 7, 'DKV'), (3, 7, 'Shell'), (4, 8, 'OMV');
            INSERT INTO "TransactionTypeCountries" VALUES
                (1, 19, '30048650'), (1, 23, 'DE119375450'), (3, 19, '10891810'), (4, 19, '10542925');
            """,
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        await _app.DisposeAsync();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task A_configured_navigation_reads_the_configured_table_not_the_one_its_name_ends_in()
    {
        List<(string, string?, string?)> rows;
        using (Tenant.Use(Acme))
        {
            rows = [.. (await TaxedGood
                    .Select(g => new { g.Name, Tax = g.ForeignTaxProductCategory!.Name, Category = g.ProductCategory!.Name }))
                .Select(static r => (r.Name, r.Tax, r.Category))];
        }

        Assert.Equal([("Diesel", "Road tax", "Fuel")], rows);
    }

    [Fact]
    public async Task The_configured_target_reads_back_the_rows_that_point_at_it()
    {
        List<string> names;
        using (Tenant.Use(Acme))
        {
            names = await ForeignTaxCategory.Where(c => c.TaxedGoods.Any()).OrderBy(c => c.Id).Select(c => c.Name);
        }

        Assert.Equal(["Road tax"], names);
    }

    [Fact]
    public async Task A_legacy_named_key_part_reaches_its_provider()
    {
        List<string> providers;
        using (Tenant.Use(Acme))
        {
            providers = await ProviderCountry.Where(c => c.CountryId == 19).OrderBy(c => c.TransactionTypeId)
                .Select(c => c.Provider.Name);
        }

        Assert.Equal(["DKV", "Shell"], providers);
    }

    [Fact]
    public async Task A_provider_reads_its_vat_numbers_through_the_composite_keyed_rows()
    {
        List<string> numbers;
        using (Tenant.Use(Acme))
        {
            numbers = await Provider.Where(p => p.Id == 1)
                .Select(p => p.ProviderCountries.Where(c => c.Country.Id == 23).Select(c => c.VatNumber).First());
        }

        Assert.Equal(["DE119375450"], numbers);
    }

    [Fact]
    public async Task Only_the_tenants_own_providers_are_counted_with_their_vat_numbers()
    {
        List<int> counts;
        using (Tenant.Use(Acme))
        {
            counts = await Provider.OrderBy(p => p.Id).Select(p => p.ProviderCountries.Count);
        }

        Assert.Equal([2, 1], counts);
    }
}
