using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests.Reports;

/// <summary>A column of the report catalog, which several report rows point at.</summary>
public sealed class CatalogColumn : Aggregate<int>
{
    public string Name { get; private set; } = "";

    public static void Configure(EntityTypeBuilder<CatalogColumn> builder) => builder.ToTable("CatalogColumns");
}

/// <summary>A report column's total: the column it sums and the column it is shown under, both catalog columns.</summary>
public sealed class ReportColumnTotal : Aggregate<int>
{
    public string Label { get; private set; } = "";

    public int ReportColumnColumnId { get; private set; }

    public int? TotalColumnId { get; private set; }

    public static void Configure(EntityTypeBuilder<ReportColumnTotal> builder)
    {
        builder.ToTable("ReportColumnTotals");
        builder.HasOne<CatalogColumn>().WithMany().HasForeignKey(t => t.ReportColumnColumnId);
        builder.HasOne<CatalogColumn>().WithMany().HasForeignKey(t => t.TotalColumnId);
    }
}

/// <summary>The context the report tests open.</summary>
public sealed class ReportsContext(DbContextOptions<ReportsContext> options) : RaskDbContext(options);

/// <summary>
/// Two configured ids to one catalog column, neither ending in its whole name, each give it a collection of their own:
/// <c>ReportColumnTotals</c> for the first, <c>ReportColumnTotalsTotal</c> for the second's own word.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class InverseCollectionNameTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-inverse-names-{Guid.NewGuid():N}.db");
    private ServiceProvider _app = null!;

    public async ValueTask InitializeAsync()
    {
        var connection = $"Data Source={_dbPath};Pooling=False";
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<ReportsContext>();
        services.AddDbContextFactory<ReportsContext>((sp, o) => o
            .UseSqlite(connection)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite(connection));

        _app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(_app);

        await using var context = await _app.GetRequiredService<IDbContextFactory<ReportsContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "CatalogColumns" ("Id" INTEGER NOT NULL PRIMARY KEY, "Name" TEXT NOT NULL);
            CREATE TABLE "ReportColumnTotals" (
                "Id" INTEGER NOT NULL PRIMARY KEY, "Label" TEXT NOT NULL,
                "ReportColumnColumnId" INTEGER NOT NULL, "TotalColumnId" INTEGER NULL);
            INSERT INTO "CatalogColumns" VALUES (1, 'Net'), (2, 'Gross'), (3, 'Sum');
            INSERT INTO "ReportColumnTotals" VALUES (10, 'Net total', 1, 3), (11, 'Gross total', 2, 3), (12, 'Plain', 2, NULL);
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
    public async Task The_first_keys_collection_reads_the_rows_that_point_through_it()
    {
        var labels = await CatalogColumn.OrderBy(c => c.Id)
            .Select(c => string.Join(",", c.ReportColumnTotals.OrderBy(t => t.Id).Select(t => t.Label)));

        Assert.Equal(["Net total", "Gross total,Plain", ""], labels);
    }

    [Fact]
    public async Task The_second_keys_collection_reads_only_the_rows_that_point_through_it()
    {
        var counts = await CatalogColumn.OrderBy(c => c.Id).Select(c => c.ReportColumnTotalsTotal.Count);

        Assert.Equal([0, 0, 2], counts);
    }

    [Fact]
    public async Task Both_collections_filter_in_one_query()
    {
        var names = await CatalogColumn
            .Where(c => c.ReportColumnTotals.Any(t => t.Label == "Plain") || c.ReportColumnTotalsTotal.Any())
            .OrderBy(c => c.Id)
            .Select(c => c.Name);

        Assert.Equal(["Gross", "Sum"], names);
    }

    [Fact]
    public async Task Each_key_still_reaches_its_own_column_forward()
    {
        var rows = await ReportColumnTotal.Where(t => t.TotalColumnId != null).OrderBy(t => t.Id)
            .Select(t => new { Shown = t.ReportColumnColumn.Name, Sum = t.TotalColumn!.Name });

        Assert.Equal([("Net", "Sum"), ("Gross", "Sum")], rows.Select(static r => (r.Shown, r.Sum)));
    }
}
