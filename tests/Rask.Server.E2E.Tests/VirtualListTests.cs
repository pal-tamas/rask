using Microsoft.Playwright;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     Measured virtual lists over the real socket, at a desktop's width and a phone's: rows of one to four lines open at
///     <c>InitialItemIndex</c>, <c>ScrollTo</c> lands its row at the top, the lists scroll true to either end with no
///     bare box, a log anchored to its end follows its tail until scrolled up, and a table's rows are held by the runtime
///     (#1326). The browser host's half is <c>VirtualListTests</c> in <c>Rask.Wasm.E2E.Tests</c>.
/// </summary>
public sealed class VirtualListTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    public static TheoryData<int> Widths => [1280, 390];

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task A_list_opens_with_its_initial_item_at_the_top_of_its_box(int width)
    {
        await using var session = await OpenAsync(width);

        await VirtualListJourneys.OpensAtAsync(session.Page, "#mixed-box", VirtualListPage.Initial);
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task Scroll_to_lands_its_row_at_the_top_and_finishes_once_it_is_there(int width)
    {
        await using var session = await OpenAsync(width);

        await VirtualListJourneys.ScrollsToAsync(session.Page, "#mixed-box", "#go", "#landed", VirtualListPage.Target);
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task Rows_of_mixed_heights_scroll_true_to_either_end_with_no_bare_box_and_no_drift(int width)
    {
        await using var session = await OpenAsync(width);

        await VirtualListJourneys.ScrollsTrueAsync(session.Page, "#mixed-box", VirtualListPage.Rows);
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task A_log_anchored_to_its_end_follows_its_tail_and_stops_once_scrolled_up(int width)
    {
        await using var session = await OpenAsync(width);

        await VirtualListJourneys.FollowsTheTailAsync(
            session.Page, "#log-box", "#append", "#log-count", VirtualListPage.FirstLog, VirtualListPage.Added);
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task A_table_s_rows_are_held_by_the_runtime_and_scroll_true(int width)
    {
        await using var session = await OpenAsync(width);

        await VirtualListJourneys.HoldsATableAsync(session.Page, "#table-box", VirtualListPage.Rows);
    }

    private async Task<HookSession> OpenAsync(int width)
    {
        var session = await HookSession.OpenAsync<VirtualListPage>(
            playwright, new BrowserNewContextOptions { ViewportSize = new() { Width = width, Height = 900 } });
        await session.HooksLoadedAsync();
        return session;
    }
}
