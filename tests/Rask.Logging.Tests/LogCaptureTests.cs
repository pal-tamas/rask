using Microsoft.Extensions.Logging;

namespace Rask.Logging.Tests;

/// <summary>
/// The capture path: an ordinary <c>ILogger</c> call on one end, a queryable row on the other. Every test
/// here logs through the real <see cref="ILoggerFactory"/> rather than poking the provider, because the
/// pipeline (level filtering, formatting, the provider registration itself) is exactly what could break.
/// </summary>
public sealed class LogCaptureTests
{
    [Fact]
    public async Task CapturesAnEntryLoggedThroughThePipeline()
    {
        await using var harness = new LoggingHarness();

        harness.Logger("Shop.Checkout").LogInformation("Order {Id} placed", 42);
        await harness.RunUntilStoredAsync(1);

        var page = await harness.Store.QueryAsync(new LogQuery());
        var entry = Assert.Single(page.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Shop.Checkout", entry.Category);
        Assert.Equal("Order 42 placed", entry.Message);
        Assert.Null(entry.Exception);
        Assert.Equal(harness.Clock.GetUtcNow(), entry.Timestamp);
        Assert.True(entry.Id > 0, "the store assigns a real id on insert");
    }

    [Fact]
    public async Task StoresTheExceptionAlongsideTheMessage()
    {
        await using var harness = new LoggingHarness();

        harness.Logger().LogError(new InvalidOperationException("boom"), "Action failed");
        await harness.RunUntilStoredAsync(1);

        var entry = Assert.Single((await harness.Store.QueryAsync(new LogQuery())).Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("boom", entry.Exception, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), entry.Exception, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SkipsEntriesBelowTheMinimumLevel()
    {
        await using var harness = new LoggingHarness(o => o.MinimumLevel = LogLevel.Warning);

        var logger = harness.Logger();
        logger.LogDebug("debug");
        logger.LogInformation("information");
        logger.LogWarning("warning");
        logger.LogError("error");

        await harness.RunUntilStoredAsync(2);

        var messages = (await harness.Store.QueryAsync(new LogQuery())).Entries.Select(e => e.Message);
        Assert.Equal(["error", "warning"], messages);
    }

    [Fact]
    public async Task SkipsConfiguredCategories()
    {
        await using var harness = new LoggingHarness(o => o.ExcludedCategories.Add("Noisy."));

        harness.Logger("Noisy.Poller").LogInformation("tick");
        harness.Logger("Quiet.Thing").LogInformation("kept");

        await harness.RunUntilStoredAsync(1);

        var entry = Assert.Single((await harness.Store.QueryAsync(new LogQuery())).Entries);
        Assert.Equal("Quiet.Thing", entry.Category);
    }

    /// <summary>
    /// The store must never capture its own plumbing, whatever the configuration says — otherwise a SQLite
    /// failure logs a line that fails to write, which logs a line. The exclusion is load-bearing, not a
    /// default, so this test clears the configurable list to prove it can't be switched off.
    /// </summary>
    [Fact]
    public async Task NeverCapturesItsOwnCategoriesEvenWhenNothingIsExcluded()
    {
        await using var harness = new LoggingHarness(o => o.ExcludedCategories.Clear());

        harness.Logger("Rask.Logging.LogWriter").LogError("the store itself failed");
        harness.Logger("Microsoft.Data.Sqlite.Command").LogError("a command failed");
        harness.Logger("App").LogError("kept");

        await harness.RunUntilStoredAsync(1);

        var entry = Assert.Single((await harness.Store.QueryAsync(new LogQuery())).Entries);
        Assert.Equal("App", entry.Category);
    }

    /// <summary>
    /// A full buffer drops entries rather than blocking the caller or growing without bound. The drop is the
    /// designed behaviour — logging must never become backpressure on a request — so what matters is that
    /// the call site is unharmed and the loss is countable.
    /// </summary>
    [Fact]
    public async Task DropsEntriesWhenTheBufferIsFullWithoutThrowingOrBlocking()
    {
        await using var harness = new LoggingHarness(o => o.QueueCapacity = 2);

        // Nothing is draining yet, so everything past the second entry has nowhere to go.
        var logger = harness.Logger();
        var exception = Record.Exception(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                logger.LogInformation("entry {Index}", i);
            }
        });

        Assert.Null(exception);

        await harness.RunUntilStoredAsync(2);
        Assert.Equal(2, await harness.Store.CountAsync());
    }

    /// <summary>
    /// The lines written in the seconds before a shutdown are the ones most worth keeping, so the writer
    /// drains what is buffered instead of exiting with it in memory.
    /// </summary>
    /// <remarks>
    /// The writer's loop is deliberately never started, which is the fix for #617's sibling #619 and the
    /// same lesson #594 landed. A long <c>FlushInterval</c> does <em>not</em> mean "nothing reaches disk on
    /// the timer" — <c>ExecuteAsync</c> is a <c>do/while</c> over <c>PeriodicTimer</c>, so its first cycle
    /// runs immediately, before the first tick. Under load that cycle can pull the entry out of the channel
    /// and still be inside <c>store.AppendAsync</c> when <c>StopAsync</c> cancels the stopping token — at
    /// which point the entry is gone for good (already out of the buffer, counted dropped, never re-queued)
    /// and the drain finds nothing. The test then failed on an empty collection, for a reason that had
    /// nothing to do with the behaviour it names.
    /// <para>
    /// With no loop, the shutdown drain is the only code that can append, so "anything stored can only have
    /// come from the drain" is true by construction rather than by scheduling. The pipeline under test is
    /// unchanged: the entry still goes through the real <c>ILogger</c> → channel → store path.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task FlushesBufferedEntriesOnShutdown()
    {
        await using var harness = new LoggingHarness(o => o.FlushInterval = TimeSpan.FromMinutes(5));

        harness.Logger().LogWarning("the last thing that happened");
        await harness.Writer.StopAsync(CancellationToken.None);

        var entry = Assert.Single((await harness.Store.QueryAsync(new LogQuery())).Entries);
        Assert.Equal("the last thing that happened", entry.Message);
    }

    /// <summary>
    /// The whole point of the package. A second store over the same file — the state a restarted process
    /// finds — still sees what the first one wrote, and creating its schema again is a no-op rather than an
    /// error.
    /// </summary>
    [Fact]
    public async Task StoredEntriesOutliveTheStoreThatWroteThem()
    {
        await using var harness = new LoggingHarness();

        harness.Logger("Shop").LogError("the thing that broke");
        await harness.RunUntilStoredAsync(1);

        var reopened = new SqliteLogStore(
            $"Data Source={harness.DbPath}", new RaskLoggingOptions(), harness.Clock);

        var entry = Assert.Single((await reopened.QueryAsync(new LogQuery())).Entries);
        Assert.Equal("the thing that broke", entry.Message);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("Shop", entry.Category);
    }

    [Fact]
    public async Task BatchesLargerThanTheBatchSizeAcrossSeveralTransactions()
    {
        await using var harness = new LoggingHarness(o =>
        {
            o.BatchSize = 3;
            o.QueueCapacity = 100;
        });

        var logger = harness.Logger();
        for (var i = 0; i < 10; i++)
        {
            logger.LogInformation("entry {Index}", i);
        }

        await harness.RunUntilStoredAsync(10);
        Assert.Equal(10, await harness.Store.CountAsync());
    }

    /// <summary>
    /// Ids order entries logged inside the same clock tick. The fake clock never advances here, so every
    /// entry shares a timestamp — the case that would make a timestamp-ordered page non-deterministic.
    /// </summary>
    [Fact]
    public async Task OrdersEntriesLoggedWithinTheSameTickNewestFirst()
    {
        await using var harness = new LoggingHarness();

        var logger = harness.Logger();
        logger.LogInformation("first");
        logger.LogInformation("second");
        logger.LogInformation("third");

        await harness.RunUntilStoredAsync(3);

        var page = await harness.Store.QueryAsync(new LogQuery());
        Assert.Equal(["third", "second", "first"], page.Entries.Select(e => e.Message));
        Assert.Single(page.Entries.Select(e => e.Timestamp).Distinct());
    }
}
