using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests.Refunds;

/// <summary>A refund category, holding its countries by a column the table named after an older type.</summary>
public sealed class RefundCategory : Aggregate<int>
{
    private readonly List<RefundCategoryCountry> _countries = [];

    public string Name { get; private set; } = "";

    public IReadOnlyList<RefundCategoryCountry> Countries => _countries;

    public static void Configure(EntityTypeBuilder<RefundCategory> builder)
    {
        builder.ToTable("RefundProductCategories");
        builder.HasMany(c => c.Countries).WithOne().HasForeignKey(row => row.RefundProductCategoryId);
    }
}

/// <summary>A country a category is claimed in, its category's part named after the table's column.</summary>
public sealed partial class RefundCategoryCountry : Entity<(int RefundProductCategoryId, Excise.Country)>
{
    private readonly List<RefundCategoryCountryRate> _rates = [];

    public int RefundProductCategoryId { get; private set; }

    public IReadOnlyList<RefundCategoryCountryRate> Rates => _rates;

    public static void Configure(EntityTypeBuilder<RefundCategoryCountry> builder)
    {
        builder.ToTable("RefundProductCategoryCountries");
        builder.HasOne<Excise.Country>().WithMany().HasForeignKey(row => row.CountryId);
        builder.HasMany(c => c.Rates).WithOne().HasForeignKey(r => new { r.RefundProductCategoryId, r.CountryId });
    }
}

/// <summary>A year's rate of a category in a country: a grandchild keyed by its holder's columns and the year.</summary>
public sealed partial class RefundCategoryCountryRate : Entity<(int RefundProductCategoryId, int CountryId, int Year)>
{
    public int RefundProductCategoryId { get; private set; }

    public int CountryId { get; private set; }

    public int Year { get; private set; }

    public decimal Percent { get; private set; }

    public static void Configure(EntityTypeBuilder<RefundCategoryCountryRate> builder) =>
        builder.ToTable("RefundProductCategoryCountryRates");
}

/// <summary>A rebate category, its countries keyed by the category itself.</summary>
public sealed class RebateCategory : Aggregate<int>
{
    private readonly List<RebateCategoryCountry> _countries = [];

    public IReadOnlyList<RebateCategoryCountry> Countries => _countries;

    public static void Configure(EntityTypeBuilder<RebateCategory> builder)
    {
        builder.ToTable("RebateCategories");
        builder.HasMany(c => c.Countries).WithOne().HasForeignKey(row => row.RebateCategoryId);
    }
}

/// <summary>A country a rebate category is claimed in, keyed by the pair of aggregates.</summary>
public sealed partial class RebateCategoryCountry : Entity<(RebateCategory, Excise.Country)>
{
    public static void Configure(EntityTypeBuilder<RebateCategoryCountry> builder) =>
        builder.ToTable("RebateCategoryCountries");
}

/// <summary>The context the refund tests open.</summary>
public sealed class RefundContext(DbContextOptions<RefundContext> options) : RaskDbContext(options);

/// <summary>
/// A child a root holds by a relationship its <c>Configure</c> declares reads back to the root over that one column —
/// a key part named after the table's column, the aggregate spelled as a part, or a grandchild's columns — with no
/// shadow column the table has not got.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class HeldChildRelationshipReadTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-held-child-{Guid.NewGuid():N}.db");
    private ServiceProvider _app = null!;

    public async ValueTask InitializeAsync()
    {
        var connection = $"Data Source={_dbPath};Pooling=False";
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<RefundContext>();
        services.AddDbContextFactory<RefundContext>((sp, o) => o
            .UseSqlite(connection)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o
            .UseSqlite(connection)
            .ConfigureWarnings(static w => w.Throw(CoreEventId.ShadowForeignKeyPropertyCreated)));

        _app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(_app);

        await using var context = await _app.GetRequiredService<IDbContextFactory<RefundContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "Countries" ("Id" INTEGER NOT NULL PRIMARY KEY);
            CREATE TABLE "RefundProductCategories" ("Id" INTEGER NOT NULL PRIMARY KEY, "Name" TEXT NOT NULL);
            CREATE TABLE "RefundProductCategoryCountries" (
                "RefundProductCategoryId" INTEGER NOT NULL, "CountryId" INTEGER NOT NULL,
                PRIMARY KEY ("RefundProductCategoryId", "CountryId"));
            CREATE TABLE "RefundProductCategoryCountryRates" (
                "RefundProductCategoryId" INTEGER NOT NULL, "CountryId" INTEGER NOT NULL, "Year" INTEGER NOT NULL,
                "Percent" TEXT NOT NULL, PRIMARY KEY ("RefundProductCategoryId", "CountryId", "Year"));
            CREATE TABLE "RebateCategories" ("Id" INTEGER NOT NULL PRIMARY KEY);
            CREATE TABLE "RebateCategoryCountries" (
                "RebateCategoryId" INTEGER NOT NULL, "CountryId" INTEGER NOT NULL,
                PRIMARY KEY ("RebateCategoryId", "CountryId"));
            INSERT INTO "Countries" VALUES (19), (23);
            INSERT INTO "RefundProductCategories" VALUES (1, 'Diesel'), (2, 'Petrol');
            INSERT INTO "RefundProductCategoryCountries" VALUES (1, 19), (1, 23), (2, 23);
            INSERT INTO "RefundProductCategoryCountryRates" VALUES (1, 23, 2026, '21.5');
            INSERT INTO "RebateCategories" VALUES (5), (6);
            INSERT INTO "RebateCategoryCountries" VALUES (5, 19), (6, 19), (6, 23);
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
    public async Task A_named_key_part_the_root_holds_its_children_by_is_the_foreign_key_back_to_the_root()
    {
        await using var read = await OpenReadAsync();

        var face = read.Model.FindEntityType(typeof(RefundCategoryCountryRead))!;
        var toRoot = Assert.Single(face.GetForeignKeys(), static k => k.PrincipalEntityType.ClrType == typeof(RefundCategoryRead));

        Assert.DoesNotContain(face.GetProperties(), static p => p.IsShadowProperty());
        Assert.Equal(["RefundProductCategoryId"], toRoot.Properties.Select(static p => p.Name));
    }

    [Fact]
    public async Task A_country_reads_its_category_through_the_named_key_part()
    {
        var names = await RefundCategoryCountry.Where(c => c.CountryId == 23).OrderBy(c => c.RefundProductCategoryId)
            .Select(c => c.RefundCategory.Name);

        Assert.Equal(["Diesel", "Petrol"], names);
    }

    [Fact]
    public async Task A_category_reads_its_countries_through_the_named_key_part()
    {
        var counts = await RefundCategory.OrderBy(c => c.Id).Select(c => c.Countries.Count);

        Assert.Equal([2, 1], counts);
    }

    [Fact]
    public async Task A_root_part_spelled_as_the_aggregate_is_the_foreign_key_back_to_the_root()
    {
        await using var read = await OpenReadAsync();

        var face = read.Model.FindEntityType(typeof(RebateCategoryCountryRead))!;
        var toRoot = Assert.Single(face.GetForeignKeys(), static k => k.PrincipalEntityType.ClrType == typeof(RebateCategoryRead));
        var counts = await RebateCategory.OrderBy(c => c.Id).Select(c => c.Countries.Count);

        Assert.DoesNotContain(face.GetProperties(), static p => p.IsShadowProperty());
        Assert.Equal(["RebateCategoryId"], toRoot.Properties.Select(static p => p.Name));
        Assert.Equal([1, 2], counts);
    }

    [Fact]
    public async Task A_grandchild_reads_back_to_the_child_holding_it_over_the_childs_columns()
    {
        await using var read = await OpenReadAsync();

        var face = read.Model.FindEntityType(typeof(RefundCategoryCountryRateRead))!;
        var toChild = Assert.Single(face.GetForeignKeys(), static k => k.PrincipalEntityType.ClrType == typeof(RefundCategoryCountryRead));
        var names = await RefundCategoryCountryRate.Select(r => r.RefundCategoryCountry.RefundCategory.Name);

        Assert.DoesNotContain(face.GetProperties(), static p => p.IsShadowProperty());
        Assert.Equal(["RefundProductCategoryId", "CountryId"], toChild.Properties.Select(static p => p.Name));
        Assert.Equal(["Diesel"], names);
    }

    private ValueTask<RaskReadDbContext> OpenReadAsync() =>
        new(_app.GetRequiredService<IDbContextFactory<RaskReadDbContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken));
}
