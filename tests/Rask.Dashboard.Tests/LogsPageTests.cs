using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Dashboard.Pages;
using Rask.Logging;
using Rask.Testing;

namespace Rask.Dashboard.Tests;

/// <summary>
/// The Logs page. The live tail must keep working with no store installed — that is the whole point of it
/// living in the dashboard package — and History must appear only when one is.
/// </summary>
public sealed class LogsPageTests
{
    [Fact]
    public async Task OffersNoHistoryWhenNoStoreIsRegistered()
    {
        await using var harness = new DashboardHarness(Batteries.None);
        Log(harness, "something happened");

        var html = Render(harness);

        Assert.Contains("something happened", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">History<", html, StringComparison.Ordinal);
        Assert.Contains("in memory only", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OffersHistoryWhenAStoreIsRegistered()
    {
        await using var store = new LogStoreFixture();
        await using var harness = store.Dashboard();
        Log(harness, "live only");

        var html = Render(harness);

        // Still the live tail by default: History is opt-in per view, so the zero-query page stays the one
        // an operator lands on.
        Assert.Contains("live only", html, StringComparison.Ordinal);
        Assert.Contains(">History<", html, StringComparison.Ordinal);
        Assert.Contains("in memory only", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Capture off with no store is the one state with nothing to show. With a store installed the page
    /// must not claim logging is off — History still works.
    /// </summary>
    [Fact]
    public async Task ReportsCaptureOffOnlyWhenThereIsAlsoNoStore()
    {
        await using var bare = new DashboardHarness(Batteries.None, configure: o => o.CaptureLogs = false);
        Assert.Contains("Log capture is off", Render(bare), StringComparison.Ordinal);

        await using var store = new LogStoreFixture();
        await using var harness = store.Dashboard(o => o.CaptureLogs = false);

        Assert.DoesNotContain("Log capture is off", Render(harness), StringComparison.Ordinal);
    }

    /// <summary>The live tail is unaffected by the History-only facets, which belong to the other mode.</summary>
    [Fact]
    public async Task LiveTailStillFiltersByLevelAndCategory()
    {
        await using var harness = new DashboardHarness(Batteries.None);
        harness.Get<ILoggerFactory>().CreateLogger("Shop").LogInformation("routine");
        harness.Get<ILoggerFactory>().CreateLogger("Shop").LogError("broken");

        var html = Render(harness, page => page.Level = nameof(LogLevel.Error));

        Assert.Contains("broken", html, StringComparison.Ordinal);
        Assert.DoesNotContain("routine", html, StringComparison.Ordinal);
    }

    // ── History mode, rendered ──────────────────────────────────────────────────────────────────────
    // PollingPanel loads on an asynchronous mount, which RaskTest did not drive (#555) — so until now no
    // dashboard page had ever been render-tested past its placeholder, and History's markup was reachable
    // only through E2E.

    [Fact]
    public async Task HistoryRendersTheStoredEntries()
    {
        await using var store = new LogStoreFixture();
        await using var harness = store.Dashboard();
        await store.AppendAsync("kept across restarts");

        var html = await RenderHistoryAsync(harness);

        Assert.Contains("kept across restarts", html, StringComparison.Ordinal);
        Assert.Contains("1 stored entries", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoryAppliesTheLevelFilterItWasGiven()
    {
        // The mapping is unit-tested through BuildQuery; this is the other half — that the query the page
        // builds is the one it actually reads with, end to end through the store.
        await using var store = new LogStoreFixture();
        await using var harness = store.Dashboard();
        await store.AppendAsync("routine", LogLevel.Information);
        await store.AppendAsync("broken", LogLevel.Error);

        var html = await RenderHistoryAsync(harness, page => page.Level = nameof(LogLevel.Error));

        Assert.Contains("broken", html, StringComparison.Ordinal);
        Assert.DoesNotContain("routine", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistorySaysSoWhenTheStoreIsEmpty()
    {
        await using var store = new LogStoreFixture();
        await using var harness = store.Dashboard();

        var html = await RenderHistoryAsync(harness);

        Assert.Contains("0 stored entries", html, StringComparison.Ordinal);
    }

    // ── The query-string → store-query mapping ──────────────────────────────────────────────────────
    // Where a filter would actually go missing. The store's own filtering is covered in Rask.Logging.Tests.

    [Fact]
    public void BuildsAQueryFromTheQueryString()
    {
        var query = LogsPage.BuildQuery("Warning", "Shop.Checkout", "declined", 3, 25);

        Assert.Equal(LogLevel.Warning, query.MinimumLevel);
        Assert.Equal("Shop.Checkout", query.Category);
        Assert.Equal("declined", query.Search);
        Assert.Equal(3, query.Page);
        Assert.Equal(25, query.PageSize);
    }

    [Fact]
    public void TreatsBlankFacetsAsNoFilter()
    {
        // An empty ?q= in a shared link means "no text filter", not "match the empty string".
        var query = LogsPage.BuildQuery(null, "  ", "", null, 25);

        Assert.Null(query.MinimumLevel);
        Assert.Null(query.Category);
        Assert.Null(query.Search);
        Assert.Equal(1, query.Page);
    }

    [Theory]
    [InlineData("error", LogLevel.Error)]
    [InlineData("ERROR", LogLevel.Error)]
    [InlineData("Information", LogLevel.Information)]
    [InlineData("nonsense", null)]
    public void ParsesTheLevelLeniently(string level, LogLevel? expected) =>
        Assert.Equal(expected, LogsPage.BuildQuery(level, null, null, null, 25).MinimumLevel);

    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(7, 7)]
    public void ClampsThePageToTheFirstOne(int? page, int expected) =>
        Assert.Equal(expected, LogsPage.BuildQuery(null, null, null, page, 25).Page);

    private static void Log(DashboardHarness harness, string message) =>
        harness.Get<ILoggerFactory>().CreateLogger("Test").LogInformation("{Message}", message);

    private static string Render(DashboardHarness harness, Action<LogsPage>? configure = null)
    {
        // ActivatorUtilities rather than `new`: the page takes its services through the constructor, and
        // this resolves them exactly as the router would.
        var page = ActivatorUtilities.CreateInstance<LogsPage>(harness.Services);
        configure?.Invoke(page);
        return RaskTest.Render(page, harness.Services).Html;
    }

    // History reads the store on PollingPanel's asynchronous mount, so the first render is the placeholder
    // — wait for the panel to report its total instead of asserting on markup that has not loaded yet.
    private static Task<string> RenderHistoryAsync(DashboardHarness harness, Action<LogsPage>? configure = null)
    {
        var page = ActivatorUtilities.CreateInstance<LogsPage>(harness.Services);
        page.View = "history";
        configure?.Invoke(page);
        return RaskTest.Render(page, harness.Services).WaitForAsync("stored entries");
    }

    /// <summary>A real log store on a temp file, plus a dashboard harness wired to it.</summary>
    private sealed class LogStoreFixture : IAsyncDisposable
    {
        private readonly string _dbPath =
            Path.Combine(Path.GetTempPath(), $"rask-dash-logs-{Guid.NewGuid():N}.db");

        public DashboardHarness Dashboard(Action<RaskDashboardOptions>? configure = null) =>
            _dashboard ??= new DashboardHarness(
                Batteries.None,
                configure: configure,
                extra: services => services.AddRaskLogging($"Data Source={_dbPath}"));

        private DashboardHarness? _dashboard;

        /// <summary>
        /// Writes straight to the store rather than through <c>ILogger</c>: the writer flushes the channel
        /// on an interval, so a logged line is not on disk yet when the page reads it.
        /// </summary>
        public Task AppendAsync(string message, LogLevel level = LogLevel.Information) =>
            _dashboard!.Get<ILogStore>().AppendAsync([
                new LogRecord(0, DateTimeOffset.UtcNow, level, "Test", 0, message, null)
            ]);

        public ValueTask DisposeAsync()
        {
            // Connections are pooled by connection string, so the file stays locked until they're released.
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(
                new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"));

            foreach (var path in new[] { _dbPath, $"{_dbPath}-wal", $"{_dbPath}-shm" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
