using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.Logging.Tests;

/// <summary>
/// What only the application-database store has to get right: not logging its own SQL into itself, keeping a line
/// the application's transaction rolled back, and refusing to boot on a model that never mapped the table.
/// </summary>
public sealed class DbContextLogStoreTests
{
    /// <summary>
    /// EF Core logs every command at Information. Unguarded, each flush's INSERT would be stored by the next flush,
    /// whose INSERT the flush after that stores — a log that grows by itself for as long as the app runs.
    /// </summary>
    [Fact]
    public async Task The_stores_own_SQL_is_not_captured_but_the_applications_is()
    {
        await using var harness = new LoggingHarness(kind: LogStoreKind.DbContext);

        // Started once for the whole test: stopping the writer completes its channel, and the application's
        // query below logs after the first wait.
        await harness.Writer.StartAsync(CancellationToken.None);
        try
        {
            harness.Logger("App.Checkout").LogInformation("the only line");
            await harness.WaitUntilAsync(async () => await harness.Store.CountAsync() >= 1);

            // Fifteen flush intervals of nothing to write but whatever the store logged about itself.
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            Assert.Equal(1, await harness.Store.CountAsync());
            Assert.Equal(["App.Checkout"], await harness.Store.CategoriesAsync());

            // The guard is the store's own flow, not a category: the application's EF Core commands are still its log.
            await using (var db = await harness.Get<IDbContextFactory<LogTestDbContext>>().CreateDbContextAsync())
            {
                _ = await db.Widgets.CountAsync();
            }

            await harness.WaitUntilAsync(async () =>
                (await harness.Store.CategoriesAsync()).Contains("Microsoft.EntityFrameworkCore.Database.Command"));
        }
        finally
        {
            await harness.Writer.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// The line most worth keeping is the one written while a transaction fails. The store writes on a context of its
    /// own, so the application's rollback takes its own rows and leaves the log alone.
    /// </summary>
    [Fact]
    public async Task A_line_logged_inside_a_rolled_back_transaction_is_kept()
    {
        await using var harness = new LoggingHarness(kind: LogStoreKind.DbContext);
        var contexts = harness.Get<IDbContextFactory<LogTestDbContext>>();

        await using (var db = await contexts.CreateDbContextAsync())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Widgets.Add(new Widget { Name = "never committed" });
            await db.SaveChangesAsync();

            harness.Logger("App.Checkout").LogError("payment failed, rolling back");
            await transaction.RollbackAsync();
        }

        await harness.RunUntilAsync(async () =>
            (await harness.Store.SearchAsync(new LogQuery { Search = "rolling back" })).TotalCount == 1);

        await using var verify = await contexts.CreateDbContextAsync();
        Assert.Equal(0, await verify.Widgets.CountAsync());
    }

    [Fact]
    public async Task Boot_fails_naming_the_line_to_add_when_the_model_does_not_map_the_log()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<UnmappedDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        services.AddRaskLogging<UnmappedDbContext>();
        await using var provider = services.BuildServiceProvider();

        var check = provider.GetServices<IHostedService>().OfType<LogsModelCheck<UnmappedDbContext>>().Single();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => check.StartAsync(CancellationToken.None));

        Assert.Contains("modelBuilder.AddRaskLogging();", error.Message, StringComparison.Ordinal);
        Assert.Contains("not calling AddRaskLogging<UnmappedDbContext>()", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Boot_passes_when_the_model_maps_the_log()
    {
        await using var harness = new LoggingHarness(kind: LogStoreKind.DbContext);

        var check = harness.Get<IEnumerable<IHostedService>>().OfType<LogsModelCheck<LogTestDbContext>>().Single();

        await check.StartAsync(CancellationToken.None);
    }

    [Fact]
    public void The_log_table_is_mapped_with_the_file_stores_names_and_indexes()
    {
        using var db = new LogTestDbContext(
            new DbContextOptionsBuilder<LogTestDbContext>().UseSqlite("Data Source=:memory:").Options);

        var entity = db.Model.FindEntityType(typeof(LogEntry))!;

        Assert.Equal("RaskLog", entity.GetTableName());
        Assert.Equal(LogEntry.CategoryMaxLength, entity.FindProperty(nameof(LogEntry.Category))!.GetMaxLength());
        Assert.Equal(
            ["IX_RaskLog_Category_Id", "IX_RaskLog_Level_Id", "IX_RaskLog_Timestamp"],
            entity.GetIndexes().Select(i => i.GetDatabaseName()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_category_longer_than_the_column_is_cut_to_fit_rather_than_losing_the_batch()
    {
        await using var harness = new LoggingHarness(kind: LogStoreKind.DbContext);
        var category = new string('c', LogEntry.CategoryMaxLength + 88);

        await harness.Store.AppendAsync(
            [new LogRecord(0, harness.Clock.GetUtcNow(), LogLevel.Warning, category, 0, "long category", null)]);

        var entry = Assert.Single((await harness.Store.SearchAsync(new LogQuery())).Entries);
        Assert.Equal(category[..LogEntry.CategoryMaxLength], entry.Category);
    }

    [Fact]
    public void Registering_twice_captures_each_entry_once_and_checks_the_model_once()
    {
        var services = new ServiceCollection();
        services.AddRaskLogging<LogTestDbContext>();
        services.AddRaskLogging<LogTestDbContext>();

        Assert.Single(services, d => d.ServiceType == typeof(ILoggerProvider));
        Assert.Single(services, d => d.ServiceType == typeof(ILogs));
        Assert.Single(services, d => d.ImplementationType == typeof(LogsModelCheck<LogTestDbContext>));
        Assert.Single(services, d => d.ImplementationType == typeof(LogWriter));
    }

    [Fact]
    public void The_database_drivers_are_never_captured()
    {
        var options = new RaskLoggingOptions();

        Assert.True(options.IsExcluded("Npgsql.Connection"));
        Assert.True(options.IsExcluded("Microsoft.Data.SqlClient"));
        Assert.False(options.IsExcluded("Microsoft.EntityFrameworkCore.Database.Command"));
    }

    private sealed class UnmappedDbContext(DbContextOptions<UnmappedDbContext> options) : DbContext(options);
}
