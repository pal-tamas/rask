using Microsoft.Extensions.Logging;

namespace Rask.Logging.Tests;

public sealed class FileStoreLogRetentionTests() : LogRetentionContract(LogStoreKind.File);

public sealed class DbContextStoreLogRetentionTests() : LogRetentionContract(LogStoreKind.DbContext);

/// <summary>
/// Retention. Both halves are on by default because either alone leaves the disk unbounded: age lets a log
/// storm fill it inside the window, and a row cap alone can shrink the window to minutes on a busy app. A contract,
/// run against both stores.
/// </summary>
public abstract class LogRetentionContract(LogStoreKind kind)
{
    [Fact]
    public async Task Entries_older_than_the_retention_period_are_purged()
    {
        await using var harness = Harness(o =>
        {
            o.Retention = TimeSpan.FromDays(7);
            o.MaxRows = 0;
            o.PurgeInterval = TimeSpan.FromMinutes(1);
        });

        harness.Logger().LogInformation("ancient");
        await harness.RunUntilStoredAsync(1);

        harness.Clock.Advance(TimeSpan.FromDays(8));
        await harness.RunUntilAsync(async () => await harness.Store.CountAsync() == 0);

        Assert.Equal(0, await harness.Store.CountAsync());
    }

    [Fact]
    public async Task Entries_inside_the_retention_period_are_kept()
    {
        await using var harness = Harness(o =>
        {
            o.Retention = TimeSpan.FromDays(7);
            o.MaxRows = 0;
            o.PurgeInterval = TimeSpan.FromMinutes(1);
        });

        harness.Logger().LogInformation("recent");
        await harness.RunUntilStoredAsync(1);

        harness.Clock.Advance(TimeSpan.FromDays(3));
        await harness.RunUntilAsync(() => Task.FromResult(true));

        Assert.Equal(1, await harness.Store.CountAsync());
    }

    [Fact]
    public async Task The_log_is_trimmed_to_the_newest_MaxRows()
    {
        await using var harness = Harness(o =>
        {
            o.Retention = TimeSpan.Zero;
            o.MaxRows = 5;
            o.QueueCapacity = 100;
            o.PurgeInterval = TimeSpan.FromMinutes(1);
        });
        var logger = harness.Logger();

        for (var i = 0; i < 20; i++)
        {
            logger.LogInformation("entry {Index}", i);
        }
        await harness.RunUntilAsync(async () => await harness.Store.CountAsync() == 5);

        var page = await harness.Store.SearchAsync(new LogQuery());
        Assert.Equal(
            ["entry 19", "entry 18", "entry 17", "entry 16", "entry 15"],
            page.Entries.Select(e => e.Message));
    }

    [Fact]
    public async Task Everything_is_kept_when_both_limits_are_disabled()
    {
        await using var harness = Harness(o =>
        {
            o.Retention = TimeSpan.Zero;
            o.MaxRows = 0;
            o.PurgeInterval = TimeSpan.FromMinutes(1);
        });

        harness.Logger().LogInformation("forever");
        await harness.RunUntilStoredAsync(1);

        harness.Clock.Advance(TimeSpan.FromDays(3650));
        await harness.RunUntilAsync(() => Task.FromResult(true));

        Assert.Equal(1, await harness.Store.CountAsync());
    }

    /// <summary>
    /// The sweep deletes in pages and loops until drained, so a backlog far larger than one page still
    /// clears in a single sweep rather than shrinking by 1,000 rows an hour.
    /// </summary>
    [Fact]
    public async Task A_purge_drains_a_backlog_larger_than_one_page()
    {
        await using var harness = Harness();
        var store = harness.Store;
        var now = harness.Clock.GetUtcNow();
        var records = Enumerable.Range(0, 2500)
            .Select(i => new LogRecord(0, now, LogLevel.Information, "Bulk", 0, $"entry {i}", null))
            .ToList();
        await store.AppendAsync(records);

        Assert.Equal(2500, await store.CountAsync());

        harness.Clock.Advance(TimeSpan.FromDays(30));
        var removed = await store.PurgeAsync(TimeSpan.FromDays(14), 0);

        Assert.Equal(2500, removed);
        Assert.Equal(0, await store.CountAsync());
    }

    [Fact]
    public async Task A_purge_trims_a_backlog_larger_than_one_page_to_the_row_cap()
    {
        await using var harness = Harness();
        var store = harness.Store;
        var now = harness.Clock.GetUtcNow();
        await store.AppendAsync(Enumerable.Range(0, 2500)
            .Select(i => new LogRecord(0, now, LogLevel.Information, "Bulk", 0, $"entry {i}", null))
            .ToList());

        var removed = await store.PurgeAsync(TimeSpan.Zero, 100);

        Assert.Equal(2400, removed);
        Assert.Equal(100, await store.CountAsync());
        Assert.Equal("entry 2499", (await store.SearchAsync(new LogQuery())).Entries[0].Message);
    }

    [Fact]
    public async Task A_purge_is_a_no_op_on_an_empty_store()
    {
        await using var harness = Harness();

        Assert.Equal(0, await harness.Store.PurgeAsync(TimeSpan.FromDays(1), 10));
    }

    private LoggingHarness Harness(Action<RaskLoggingOptions>? configure = null) => new(configure, kind: kind);
}
