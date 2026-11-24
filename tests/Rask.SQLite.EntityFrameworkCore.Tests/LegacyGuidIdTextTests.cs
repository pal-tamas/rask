using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

public readonly partial record struct BinId(Guid Value);

internal sealed class Bin : Aggregate<BinId>
{
    public string Label { get; private set; } = "";
}

/// <summary>A typed Guid id on SQLite, with the app's text order named or not.</summary>
internal sealed class BinContext(DbContextOptions options, bool orderTextBy) : DbContext(options)
{
    public DbSet<Bin> Bins => Set<Bin>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ModelRegistry.ApplyConventions(configurationBuilder);
        if (orderTextBy)
        {
            configurationBuilder.OrderTextBy("Hungarian_100_CI_AS");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Bin>();
        modelBuilder.ApplyRaskConventions();
    }

    // Two contexts over one file with different models: EF Core caches a model per context type unless told.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, BinModelKey>();

    internal bool OrdersText => orderTextBy;
}

internal sealed class BinModelKey : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), ((BinContext)context).OrdersText, designTime);
}

// A Guid id is written lower case on SQLite now; a row an older build (or another tool) wrote in upper case is
// still found by a Where when the column compares case-insensitively — what OrderTextBy's RASK_TEXT does.
public sealed class LegacyGuidIdTextTests : IAsyncLifetime
{
    private static readonly Guid Legacy = Guid.Parse("3e18ad13-d95e-4808-97bd-918b457c006b");

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-sqlite-guid-ids-{Guid.NewGuid():N}.db");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task An_upper_case_legacy_id_is_matched_where_the_text_order_ignores_case()
    {
        await using var context = await SeededAsync(orderTextBy: true);

        var found = await context.Bins.Where(b => b.Id == new BinId(Legacy)).Select(b => b.Label).ToListAsync(Cancellation);

        Assert.Equal(["legacy"], found);
    }

    [Fact]
    public async Task An_upper_case_legacy_id_is_not_matched_where_the_column_compares_as_written()
    {
        await using var context = await SeededAsync(orderTextBy: false);

        var found = await context.Bins.CountAsync(b => b.Id == new BinId(Legacy), Cancellation);
        var loaded = await context.Bins.Select(b => b.Id).SingleAsync(Cancellation);

        Assert.Equal(0, found);
        Assert.Equal(new BinId(Legacy), loaded);
    }

    // The table as the context creates it — with RASK_TEXT on its text columns or without — holding one row whose id
    // was written in upper case.
    private async Task<BinContext> SeededAsync(bool orderTextBy)
    {
        var context = Open(orderTextBy);
        await context.Database.EnsureCreatedAsync(Cancellation);
        var table = context.Model.FindEntityType(typeof(Bin))!.GetTableName();
#pragma warning disable EF1002 // the table name is the model's own, not input
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO \"{table}\" (\"Id\", \"Label\") VALUES ('{Legacy.ToString().ToUpperInvariant()}', 'legacy')",
            Cancellation);
#pragma warning restore EF1002
        return context;
    }

    private BinContext Open(bool orderTextBy) =>
        new(new DbContextOptionsBuilder<BinContext>().UseRaskSqliteAt($"Data Source={_dbPath}").Options, orderTextBy);
}
