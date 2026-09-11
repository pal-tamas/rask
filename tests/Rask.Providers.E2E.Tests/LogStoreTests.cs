using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Logging;
using Rask.SqlServer;

namespace Rask.Providers.E2E.Tests;

/// <summary>Something an application writes in its own transaction, for the scenario that rolls one back.</summary>
public sealed class LogWidget
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

public sealed class PgLogDbContext(DbContextOptions<PgLogDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_e2e_logs";

    public DbSet<LogWidget> Widgets => Set<LogWidget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddRaskLogging();
    }
}

public sealed class MsLogDbContext(DbContextOptions<MsLogDbContext> options) : DbContext(options)
{
    public const string DatabaseName = "rask_e2e_logs";

    public DbSet<LogWidget> Widgets => Set<LogWidget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskLogging();
}

/// <summary>
/// The application-database log store on a real server. What SQLite cannot prove: that "case-insensitive" holds on
/// a server whose comparisons are not, that the purge's page-by-id delete is right when two hosts sweep at once, and
/// that a line written on the store's own connection survives the application's rollback.
/// </summary>
internal static class LogStoreScenarios
{
    internal static async Task FiltersAndPagesAsync(ILogs store)
    {
        var now = DateTimeOffset.UtcNow;
        await store.AppendAsync(
        [
            new LogRecord(0, now.AddMinutes(-30), LogLevel.Information, "Shop.Checkout", 0, "cart opened", null,
                [new LogScopeValue("RequestId", "r1")]),
            new LogRecord(0, now.AddMinutes(-20), LogLevel.Warning, "Shop.Checkout", 0, "disk at 100% capacity", null,
                [new LogScopeValue("RequestId", "r2")]),
            new LogRecord(0, now.AddMinutes(-10), LogLevel.Error, "Shop.Orders", 7, "opaque",
                "System.InvalidOperationException: NEEDLE in the trace",
                [new LogScopeValue("RequestId", "r22"), new LogScopeValue("UserId", "u1")]),
        ]);

        Assert.Equal(3, await store.CountAsync());
        Assert.Equal(2, (await store.SearchAsync(new LogQuery { MinimumLevel = LogLevel.Warning })).TotalCount);

        // Case-insensitive on a server whose own comparisons are not (PostgreSQL), over the exception too.
        Assert.Equal(2, (await store.SearchAsync(new LogQuery { Category = "CHECKOUT" })).TotalCount);
        Assert.Equal("opaque", Assert.Single((await store.SearchAsync(new LogQuery { Search = "needle" })).Entries).Message);

        // A LIKE wildcard in the text is literal.
        Assert.Single((await store.SearchAsync(new LogQuery { Search = "100%" })).Entries);
        Assert.Empty((await store.SearchAsync(new LogQuery { Search = "%capacity%" })).Entries);

        // The scope filter is exact: r2 is not a prefix match for r22.
        var request = Assert.Single(
            (await store.SearchAsync(new LogQuery { ScopeKey = "RequestId", ScopeValue = "r2" })).Entries);
        Assert.Equal("disk at 100% capacity", request.Message);
        Assert.Equal(3, (await store.SearchAsync(new LogQuery { ScopeKey = "RequestId" })).TotalCount);

        Assert.Equal(
            "cart opened",
            Assert.Single((await store.SearchAsync(new LogQuery { To = now.AddMinutes(-25) })).Entries).Message);

        var first = await store.SearchAsync(new LogQuery { PageSize = 2 });
        Assert.Equal(2, first.PageCount);
        Assert.Equal(["opaque", "disk at 100% capacity"], first.Entries.Select(e => e.Message));

        Assert.Equal(["Shop.Checkout", "Shop.Orders"], await store.CategoriesAsync());

        // The same instant back. PostgreSQL keeps microseconds, so the comparison allows the last tick.
        var stored = first.Entries[0];
        Assert.True(
            (stored.Timestamp - now.AddMinutes(-10)).Duration() < TimeSpan.FromMilliseconds(1),
            $"stored {stored.Timestamp:O}, written {now.AddMinutes(-10):O}");
        Assert.Equal(7, stored.EventId);
        Assert.Equal("u1", stored.Scopes!.Single(s => s.Key == "UserId").Value);
    }

    internal static async Task RetentionAndRowCapAsync(ILogs store)
    {
        var old = DateTimeOffset.UtcNow.AddDays(-30);
        await store.AppendAsync(Records(2500, old, "old"));
        await store.AppendAsync(Records(300, DateTimeOffset.UtcNow, "new"));

        // More than two pages of 1,000 go in one sweep.
        Assert.Equal(2500, await store.PurgeAsync(TimeSpan.FromDays(14), 0));
        Assert.Equal(200, await store.PurgeAsync(TimeSpan.Zero, 100));

        Assert.Equal(100, await store.CountAsync());
        Assert.Equal("new 299", (await store.SearchAsync(new LogQuery())).Entries[0].Message);

        await store.ClearAsync();
        Assert.Equal(0, await store.CountAsync());
    }

    /// <summary>Two instances of the app sweep the same table at once; every row is removed, and counted, once.</summary>
    internal static async Task TwoHostsPurgingAtOnceAsync(ILogs first, ILogs second)
    {
        await first.AppendAsync(Records(5000, DateTimeOffset.UtcNow.AddDays(-30), "old"));

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sweeps = new[] { first, second }.Select(async store =>
        {
            await start.Task;
            return await store.PurgeAsync(TimeSpan.FromDays(14), 0);
        }).ToList();

        start.SetResult();
        var removed = await Task.WhenAll(sweeps);

        Assert.Equal(5000, removed.Sum());
        Assert.Equal(0, await first.CountAsync());
    }

    /// <summary>
    /// A line appended while the application's transaction is open — the moment a failing request logs why — is on
    /// the store's own connection, so the rollback that follows takes the application's rows and not the log's.
    /// </summary>
    internal static async Task ALineSurvivesTheApplicationsRollbackAsync<TContext>(
        IDbContextFactory<TContext> contexts,
        ILogs store)
        where TContext : DbContext
    {
        await using (var app = await contexts.CreateDbContextAsync())
        {
            // Both providers retry, and a retrying strategy only runs a hand-opened transaction inside itself.
            await app.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await app.Database.BeginTransactionAsync();
                app.Set<LogWidget>().Add(new LogWidget { Name = "never committed" });
                await app.SaveChangesAsync();

                await store.AppendAsync(
                    [new LogRecord(0, DateTimeOffset.UtcNow, LogLevel.Error, "App.Checkout", 0, "payment failed, rolling back", null)]);

                await transaction.RollbackAsync();
            });
        }

        Assert.Equal(1, (await store.SearchAsync(new LogQuery { Search = "rolling back" })).TotalCount);

        await using var verify = await contexts.CreateDbContextAsync();
        Assert.Equal(0, await verify.Set<LogWidget>().CountAsync());
    }

    /// <summary>PostgreSQL refuses NUL in text; one such line must cost only its NUL, not its whole batch.</summary>
    internal static async Task ANulCharacterCostsNothingButItselfAsync(ILogs store)
    {
        await store.AppendAsync(
        [
            new LogRecord(0, DateTimeOffset.UtcNow, LogLevel.Warning, "Shop.Input", 0, "user sent a\0b", "System.Exception: x\0y"),
            new LogRecord(0, DateTimeOffset.UtcNow, LogLevel.Information, "Shop.Input", 0, "an ordinary line", null),
        ]);

        var page = await store.SearchAsync(new LogQuery());
        Assert.Equal(2, page.TotalCount);

        var entry = page.Entries.Single(e => e.Level == LogLevel.Warning);
        Assert.Equal("user sent a�b", entry.Message);
        Assert.Equal("System.Exception: x�y", entry.Exception);
    }

    private static List<LogRecord> Records(int count, DateTimeOffset at, string prefix) =>
        Enumerable.Range(0, count)
            .Select(i => new LogRecord(0, at, LogLevel.Information, "Bulk", 0, $"{prefix} {i}", null))
            .ToList();
}

[Collection(PostgresCollection.Name)]
public sealed class PostgresLogStoreTests : IAsyncLifetime
{
    private readonly List<ServiceProvider> _hosts = [];

    public async Task InitializeAsync()
    {
        if (Postgres.Available)
        {
            await using var db = await NewHost().GetRequiredService<IDbContextFactory<PgLogDbContext>>().CreateDbContextAsync();
            await Postgres.ResetSchemaAsync(db, PgLogDbContext.Schema);
        }
    }

    public async Task DisposeAsync()
    {
        if (Postgres.Available)
        {
            await using var db = await NewHost().GetRequiredService<IDbContextFactory<PgLogDbContext>>().CreateDbContextAsync();
            await Postgres.DropSchemaAsync(db, PgLogDbContext.Schema);
        }

        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task Filters_ignore_case_and_pages_come_newest_first()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);
        await LogStoreScenarios.FiltersAndPagesAsync(Store(NewHost()));
    }

    [SkippableFact]
    public async Task Retention_and_the_row_cap_sweep_in_pages()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);
        await LogStoreScenarios.RetentionAndRowCapAsync(Store(NewHost()));
    }

    [SkippableFact]
    public async Task Two_hosts_purging_at_once_remove_each_row_once()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);
        await LogStoreScenarios.TwoHostsPurgingAtOnceAsync(Store(NewHost()), Store(NewHost()));
    }

    [SkippableFact]
    public async Task A_nul_character_costs_nothing_but_itself()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);
        await LogStoreScenarios.ANulCharacterCostsNothingButItselfAsync(Store(NewHost()));
    }

    [SkippableFact]
    public async Task A_line_survives_the_applications_rollback()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);
        var host = NewHost();
        await LogStoreScenarios.ALineSurvivesTheApplicationsRollbackAsync(
            host.GetRequiredService<IDbContextFactory<PgLogDbContext>>(), Store(host));
    }

    private static ILogs Store(IServiceProvider host) => host.GetRequiredService<ILogs>();

    /// <summary>One instance of the app: its own container, so its own store and its own connections.</summary>
    private ServiceProvider NewHost()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<PgLogDbContext>(o => o.UseRaskPostgres(Postgres.Required));
        services.AddRaskLogging<PgLogDbContext>();

        var host = services.BuildServiceProvider();
        _hosts.Add(host);
        return host;
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerLogStoreTests : IAsyncLifetime
{
    private readonly List<ServiceProvider> _hosts = [];

    public async Task InitializeAsync()
    {
        if (SqlServer.Available)
        {
            await using var db = await NewHost().GetRequiredService<IDbContextFactory<MsLogDbContext>>().CreateDbContextAsync();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (SqlServer.Available)
        {
            await using var db = await NewHost().GetRequiredService<IDbContextFactory<MsLogDbContext>>().CreateDbContextAsync();
            await db.Database.EnsureDeletedAsync();
        }

        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task Filters_ignore_case_and_pages_come_newest_first()
    {
        Skip.IfNot(SqlServer.Available, SqlServer.SkipReason);
        await LogStoreScenarios.FiltersAndPagesAsync(Store(NewHost()));
    }

    [SkippableFact]
    public async Task Retention_and_the_row_cap_sweep_in_pages()
    {
        Skip.IfNot(SqlServer.Available, SqlServer.SkipReason);
        await LogStoreScenarios.RetentionAndRowCapAsync(Store(NewHost()));
    }

    [SkippableFact]
    public async Task Two_hosts_purging_at_once_remove_each_row_once()
    {
        Skip.IfNot(SqlServer.Available, SqlServer.SkipReason);
        await LogStoreScenarios.TwoHostsPurgingAtOnceAsync(Store(NewHost()), Store(NewHost()));
    }

    [SkippableFact]
    public async Task A_nul_character_costs_nothing_but_itself()
    {
        Skip.IfNot(SqlServer.Available, SqlServer.SkipReason);
        await LogStoreScenarios.ANulCharacterCostsNothingButItselfAsync(Store(NewHost()));
    }

    [SkippableFact]
    public async Task A_line_survives_the_applications_rollback()
    {
        Skip.IfNot(SqlServer.Available, SqlServer.SkipReason);
        var host = NewHost();
        await LogStoreScenarios.ALineSurvivesTheApplicationsRollbackAsync(
            host.GetRequiredService<IDbContextFactory<MsLogDbContext>>(), Store(host));
    }

    private static ILogs Store(IServiceProvider host) => host.GetRequiredService<ILogs>();

    private ServiceProvider NewHost()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<MsLogDbContext>(o => o.UseRaskSqlServer(SqlServer.Database(MsLogDbContext.DatabaseName)));
        services.AddRaskLogging<MsLogDbContext>();

        var host = services.BuildServiceProvider();
        _hosts.Add(host);
        return host;
    }
}
