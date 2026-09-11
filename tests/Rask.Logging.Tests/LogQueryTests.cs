using Microsoft.Extensions.Logging;

namespace Rask.Logging.Tests;

public sealed class FileStoreLogQueryTests() : LogQueryContract(LogStoreKind.File);

public sealed class DbContextStoreLogQueryTests() : LogQueryContract(LogStoreKind.DbContext);

/// <summary>
/// The read side: filtering and paging over the stored log. A contract, run against both stores — what a
/// <see cref="LogQuery"/> means must not depend on where the log happens to be kept.
/// </summary>
public abstract class LogQueryContract(LogStoreKind kind)
{
    [Fact]
    public async Task FiltersByMinimumLevel()
    {
        await using var harness = await SeededAsync();

        var page = await harness.Store.SearchAsync(new LogQuery { MinimumLevel = LogLevel.Warning });

        Assert.All(page.Entries, e => Assert.True(e.Level >= LogLevel.Warning));
        Assert.Equal(page.Entries.Count, page.TotalCount);
    }

    [Fact]
    public async Task FiltersByCategorySubstring()
    {
        await using var harness = await SeededAsync();

        var page = await harness.Store.SearchAsync(new LogQuery { Category = "checkout" });

        Assert.NotEmpty(page.Entries);
        Assert.All(page.Entries, e => Assert.Contains("Checkout", e.Category, StringComparison.Ordinal));
    }

    [Fact]
    public async Task FiltersBySearchAcrossMessageAndException()
    {
        await using var harness = Harness();
        harness.Logger().LogInformation("nothing to see");
        harness.Logger().LogError(new InvalidOperationException("needle in the trace"), "opaque message");
        await harness.RunUntilStoredAsync(2);

        var page = await harness.Store.SearchAsync(new LogQuery { Search = "needle" });

        var entry = Assert.Single(page.Entries);
        Assert.Equal("opaque message", entry.Message);
    }

    /// <summary>
    /// <see cref="LogQuery.Search"/> promises to ignore case, and providers disagree on whether a plain comparison
    /// does — PostgreSQL's does not.
    /// </summary>
    [Fact]
    public async Task SearchIgnoresCase()
    {
        await using var harness = Harness();
        harness.Logger().LogInformation("Disk Nearly FULL");
        await harness.RunUntilStoredAsync(1);

        Assert.Single((await harness.Store.SearchAsync(new LogQuery { Search = "nearly full" })).Entries);
    }

    /// <summary>
    /// A search for <c>100%</c> must mean a literal percent sign. Unescaped it is a LIKE wildcard, and the
    /// filter would quietly match everything — the kind of bug that looks like the filter simply not working.
    /// </summary>
    [Fact]
    public async Task TreatsLikeWildcardsInTheSearchAsLiteralText()
    {
        await using var harness = Harness();
        harness.Logger().LogInformation("disk at 100% capacity");
        harness.Logger().LogInformation("everything is fine");
        await harness.RunUntilStoredAsync(2);

        Assert.Single((await harness.Store.SearchAsync(new LogQuery { Search = "100%" })).Entries);
        Assert.Empty((await harness.Store.SearchAsync(new LogQuery { Search = "%fine%" })).Entries);
        Assert.Empty((await harness.Store.SearchAsync(new LogQuery { Search = "ever_thing" })).Entries);
    }

    [Fact]
    public async Task FiltersByTimeRange()
    {
        await using var harness = Harness();
        var start = harness.Clock.GetUtcNow();

        harness.Logger().LogInformation("early");
        harness.Clock.Advance(TimeSpan.FromHours(2));
        harness.Logger().LogInformation("late");
        await harness.RunUntilStoredAsync(2);

        var recent = await harness.Store.SearchAsync(new LogQuery { From = start.AddHours(1) });
        Assert.Equal("late", Assert.Single(recent.Entries).Message);

        var old = await harness.Store.SearchAsync(new LogQuery { To = start.AddHours(1) });
        Assert.Equal("early", Assert.Single(old.Entries).Message);
    }

    [Fact]
    public async Task PagesNewestFirstAndReportsTheTotal()
    {
        await using var harness = Harness(o => o.QueueCapacity = 100);
        var logger = harness.Logger();
        for (var i = 0; i < 10; i++)
        {
            logger.LogInformation("entry {Index}", i);
        }

        await harness.RunUntilStoredAsync(10);

        var first = await harness.Store.SearchAsync(new LogQuery { PageSize = 4 });
        Assert.Equal(10, first.TotalCount);
        Assert.Equal(3, first.PageCount);
        Assert.Equal(["entry 9", "entry 8", "entry 7", "entry 6"], first.Entries.Select(e => e.Message));

        var last = await harness.Store.SearchAsync(new LogQuery { PageSize = 4, Page = 3 });
        Assert.Equal(["entry 1", "entry 0"], last.Entries.Select(e => e.Message));
    }

    [Fact]
    public async Task APagePastTheEndIsEmptyButKeepsTheTotal()
    {
        await using var harness = await SeededAsync();

        var page = await harness.Store.SearchAsync(new LogQuery { PageSize = 2, Page = 9 });

        Assert.Empty(page.Entries);
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task ReturnsAnEmptyPageWhenNothingMatches()
    {
        await using var harness = await SeededAsync();

        var page = await harness.Store.SearchAsync(new LogQuery { Search = "no such text anywhere" });

        Assert.Empty(page.Entries);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.PageCount);
    }

    [Fact]
    public async Task ReturnsDistinctSortedCategories()
    {
        await using var harness = await SeededAsync();

        var categories = await harness.Store.CategoriesAsync();

        Assert.Equal(["Shop.Checkout", "Shop.Orders"], categories);
    }

    [Fact]
    public async Task ClearRemovesEverything()
    {
        await using var harness = await SeededAsync();

        await harness.Store.ClearAsync();

        Assert.Equal(0, await harness.Store.CountAsync());
        Assert.Empty(await harness.Store.CategoriesAsync());
    }

    /// <summary>Querying a store nothing has written to yet returns empty rather than failing.</summary>
    [Fact]
    public async Task QueryingAnUntouchedStoreReturnsEmpty()
    {
        await using var harness = Harness();

        var page = await harness.Store.SearchAsync(new LogQuery());

        Assert.Empty(page.Entries);
        Assert.Equal(0, await harness.Store.CountAsync());
    }

    /// <summary>Every entry field survives the store, the timestamp as the same UTC instant.</summary>
    [Fact]
    public async Task AnEntryRoundTripsEveryField()
    {
        await using var harness = Harness();
        var at = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(2)).AddTicks(1_234_560);

        await harness.Store.AppendAsync(
            [new LogRecord(0, at, LogLevel.Critical, "Shop.Payments", 42, "card declined", "System.Exception: boom",
                [new LogScopeValue("RequestId", "r1")])]);

        var entry = Assert.Single((await harness.Store.SearchAsync(new LogQuery())).Entries);
        Assert.True(entry.Id > 0);
        Assert.Equal(at.UtcTicks, entry.Timestamp.UtcTicks);
        Assert.Equal(LogLevel.Critical, entry.Level);
        Assert.Equal("Shop.Payments", entry.Category);
        Assert.Equal(42, entry.EventId);
        Assert.Equal("card declined", entry.Message);
        Assert.Equal("System.Exception: boom", entry.Exception);
        Assert.Equal([new LogScopeValue("RequestId", "r1")], entry.Scopes);
    }

    /// <summary>
    /// A NUL in logged text — a user-supplied value, a buffer's <c>ToString()</c> — is stored as U+FFFD by both stores.
    /// PostgreSQL refuses NUL in text, and the refusal fails the whole batch, not just the one line.
    /// </summary>
    [Fact]
    public async Task ANulCharacterIsStoredAsTheReplacementCharacterAndCostsNothingElse()
    {
        await using var harness = Harness();
        var now = harness.Clock.GetUtcNow();

        await harness.Store.AppendAsync(
        [
            new LogRecord(0, now, LogLevel.Warning, "Shop\0Input", 0, "user sent a\0b", "System.Exception: x\0y"),
            new LogRecord(0, now, LogLevel.Information, "Shop.Input", 0, "an ordinary line", null),
        ]);

        var page = await harness.Store.SearchAsync(new LogQuery());
        Assert.Equal(2, page.TotalCount);

        var entry = page.Entries.Single(e => e.Level == LogLevel.Warning);
        Assert.Equal("user sent a�b", entry.Message);
        Assert.Equal("System.Exception: x�y", entry.Exception);
        Assert.Equal("Shop�Input", entry.Category);
    }

    private LoggingHarness Harness(Action<RaskLoggingOptions>? configure = null) => new(configure, kind: kind);

    private async Task<LoggingHarness> SeededAsync()
    {
        var harness = Harness();
        harness.Logger("Shop.Checkout").LogInformation("cart opened");
        harness.Logger("Shop.Checkout").LogWarning("payment retried");
        harness.Logger("Shop.Orders").LogError("order rejected");
        await harness.RunUntilStoredAsync(3);
        return harness;
    }
}
