using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using Rask.Wasm.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Wasm.E2E.Tests;

/// <summary>
///     Measured virtual lists drawn by the browser host, at a desktop's width and a phone's: the same page and the same
///     journeys as the Server host's <c>VirtualListTests</c> (<c>VirtualListPage</c>, linked into the fixture, and
///     <c>VirtualListJourneys</c>) — in WebAssembly the scroll is heard in the same process that draws the rows, so
///     what the socket's round trip hid would show here (#1326).
/// </summary>
public sealed class VirtualListTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    // VirtualListPage's numbers: the fixture is published, so the page's own constants are not referenced from here.
    private const int Rows = 3000;
    private const int Initial = 1234;
    private const int Target = 2345;
    private const int FirstLog = 400;
    private const int Added = 5;

    // The first WASM boot downloads and starts the runtime.
    private static readonly LocatorAssertionsToBeVisibleOptions Boot = new() { Timeout = 60_000 };

    public static TheoryData<int> Widths => [1280, 390];

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task A_list_opens_with_its_initial_item_at_the_top_of_its_box(int width) =>
        await WithPageAsync(width, page => VirtualListJourneys.OpensAtAsync(page, "#mixed-box", Initial));

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task Scroll_to_lands_its_row_at_the_top_and_finishes_once_it_is_there(int width) =>
        await WithPageAsync(width, page => VirtualListJourneys.ScrollsToAsync(page, "#mixed-box", "#go", "#landed", Target));

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task Rows_of_mixed_heights_scroll_true_to_either_end_with_no_bare_box_and_no_drift(int width) =>
        await WithPageAsync(width, page => VirtualListJourneys.ScrollsTrueAsync(page, "#mixed-box", Rows));

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task A_log_anchored_to_its_end_follows_its_tail_and_stops_once_scrolled_up(int width) =>
        await WithPageAsync(width, page => VirtualListJourneys.FollowsTheTailAsync(page, "#log-box", "#append", "#log-count", FirstLog, Added));

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task A_table_s_rows_are_held_by_the_runtime_and_scroll_true(int width) =>
        await WithPageAsync(width, page => VirtualListJourneys.HoldsATableAsync(page, "#table-box", Rows));

    private async Task WithPageAsync(int width, Func<IPage, Task> journey)
    {
        await using var host = await SignInHost.StartAsync();
        await using var context = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = 900 } });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(host.BaseUrl + "/virtual");
        await Expect(page.Locator("#mixed-box [data-rask-virtual]")).ToBeVisibleAsync(Boot);
        await page.WaitForFunctionAsync(
            "() => performance.getEntriesByType('resource').some(e => e.name.includes('rask-hooks') && e.responseEnd > 0) && window.__raskHookSeam && !window.__raskHookSeam.missed");

        await journey(page);

        Assert.Empty(errors);
    }
}
