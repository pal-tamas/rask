using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Dashboard.Pages;
using Rask.Logging;
using Rask.Testing;

namespace Rask.Dashboard.Tests;

/// <summary>
/// The Logs page. The live tail must keep working with no store installed — that is the whole point of it
/// living in the dashboard package — and History must appear only when one is.
/// <para>
/// The rendered assertions cover the live tail, which renders synchronously. History's own reading is
/// covered through <see cref="LogsPage.BuildQuery"/> plus the store's tests in <c>Rask.Logging.Tests</c>:
/// <c>PollingPanel</c> loads on an asynchronous mount that <c>RaskTest</c>'s bare render harness does not
/// drive to completion, which is true of every dashboard page and not specific to this one.
/// </para>
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

    /// <summary>A real log store on a temp file, plus a dashboard harness wired to it.</summary>
    private sealed class LogStoreFixture : IAsyncDisposable
    {
        private readonly string _dbPath =
            Path.Combine(Path.GetTempPath(), $"rask-dash-logs-{Guid.NewGuid():N}.db");

        public DashboardHarness Dashboard(Action<RaskDashboardOptions>? configure = null) =>
            new(Batteries.None,
                configure: configure,
                extra: services => services.AddRaskLogging($"Data Source={_dbPath}"));

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
