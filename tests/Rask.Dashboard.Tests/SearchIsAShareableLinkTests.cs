using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Routing;
using Rask.Dashboard.Pages;
using Rask.Logging;
using Rask.Testing;

namespace Rask.Dashboard.Tests;

/// <summary>
/// Both search boxes declare their term as a <c>[QueryParam]</c> and both call the result a shareable
/// link, but neither reached the URL: they assigned the property, reloaded and re-rendered (#936).
/// </summary>
/// <remarks>
///     The failure is invisible on screen — the results are correct, the box holds the term, and only the
///     address bar disagrees. What it costs is the thing the feature is for: copying the link, or
///     reloading, silently drops the search. Asserted against <c>RouteState</c> rather than markup for
///     that reason; markup was never the part that was wrong.
/// </remarks>
public sealed class SearchIsAShareableLinkTests
{
    [Fact]
    public async Task The_logs_search_reaches_the_url()
    {
        // A real store, because the search box only exists in History — a page with nothing to search
        // renders no input, and a test that missed that would pass for the wrong reason.
        await using var store = new LogStore();
        var harness = store.Dashboard();
        var route = harness.Services.GetRequiredService<RouteState>();

        var page = await HistoryAsync(harness);
        await SearchAsync(page, "timeout");

        Assert.Contains("q=timeout", route.Path + Query(route), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clearing_the_logs_search_removes_it_from_the_url()
    {
        // The half a naive fix misses: `Query` defaults to null on the link helper, which for its other
        // callers means "keep what is there". Clearing the box has to reach the URL as an ABSENT q, or
        // the address bar keeps advertising a search the page is no longer running.
        await using var store = new LogStore();
        var harness = store.Dashboard();
        var route = harness.Services.GetRequiredService<RouteState>();

        var page = await HistoryAsync(harness);
        await SearchAsync(page, "timeout");
        await SearchAsync(page, "");

        Assert.DoesNotContain("q=", route.Path + Query(route), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_cache_search_reaches_the_url()
    {
        await using var harness = new DashboardHarness(Batteries.Cache);
        var route = harness.Services.GetRequiredService<RouteState>();

        var page = RaskTest.Render(
            ActivatorUtilities.CreateInstance<CachePage>(harness.Services), harness.Services);

        await SearchAsync(page, "session:");

        Assert.Contains("q=session", route.Path + Query(route), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_storage_search_reaches_the_url()
    {
        await using var harness = new DashboardHarness(Batteries.Storage);
        var route = harness.Services.GetRequiredService<RouteState>();

        var page = RaskTest.Render(
            ActivatorUtilities.CreateInstance<StoragePage>(harness.Services), harness.Services);

        await SearchAsync(page, "invoice");

        Assert.Contains("q=invoice", route.Path + Query(route), StringComparison.Ordinal);
    }

    // History loads on PollingPanel's asynchronous mount, so the first render is a placeholder with no
    // search box on it yet.
    private static async Task<RenderedComponent> HistoryAsync(DashboardHarness harness)
    {
        var logs = ActivatorUtilities.CreateInstance<LogsPage>(harness.Services);
        logs.View = "history";
        var page = RaskTest.Render(logs, harness.Services);

        await page.WaitForAsync("stored entries");
        return page;
    }

    /// <summary>A real log store on a temp file, plus a dashboard harness wired to it.</summary>
    private sealed class LogStore : IAsyncDisposable
    {
        private readonly string _dbPath =
            Path.Combine(Path.GetTempPath(), $"rask-dash-link-{Guid.NewGuid():N}.db");

        private DashboardHarness? _dashboard;

        public DashboardHarness Dashboard() =>
            _dashboard ??= new DashboardHarness(
                Batteries.None,
                extra: services => services.AddRaskLogging($"Data Source={_dbPath}"));

        public async ValueTask DisposeAsync()
        {
            if (_dashboard is not null)
            {
                await _dashboard.DisposeAsync();
            }

            File.Delete(_dbPath);
        }
    }

    // Driven through a REAL change event on the rendered search input, not by calling the private
    // method: Navigator refuses to run outside a handler scope, and it is the framework's dispatch that
    // establishes that scope. Calling the method directly throws — which is itself worth knowing, since
    // it means a search box wired to anything but a handler could never have navigated.
    private static async Task SearchAsync(RenderedComponent page, string term)
    {
        var handler = page.HandlerIdFor("input[type=\"search\"]", "change");
        // The payload shape a change handler is fed: {"value": "…"}, exactly as the browser's own
        // listener sends it. A bare JSON string reaches the handler as an empty term, which navigates
        // to a URL with no q on it — the very failure under test, arriving from the test's own side.
        await page.InvokeAsync(handler, $$"""{"value":{{System.Text.Json.JsonSerializer.Serialize(term)}}}""");
    }

    private static string Query(RouteState route) =>
        "?" + string.Join('&', route.Query.Select(kv => $"{kv.Key}={kv.Value}"));
}
