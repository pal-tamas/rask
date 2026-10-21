using Microsoft.Playwright;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using Rask.UiTests.Live;
using static Microsoft.Playwright.Assertions;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     Eszaf #16 in a real browser, the flow of its compare/flow-reports.mjs: the report form's columns added, two grouped
///     to the front, one moved up, then a row moved down and up again, and one removed. The move back was lost.
/// </summary>
public sealed class KeyedRowMoveBackTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_kit_row_moved_down_and_then_up_again_lands_where_the_last_move_put_it()
    {
        await using var session = await HookSession.OpenAsync<ReportColumnsDraft>(playwright);
        var page = session.Page;
        await page.ClickAsync("[data-rask-key=add] >> nth=0");
        await Expect(Names(page)).ToHaveCountAsync(6);
        await Tick(page, "Mennyiség", 2);
        await Tick(page, "EO", 2);
        await Tick(page, "Jármű", 1);
        await Tick(page, "Termék", 1);
        await Expect(Names(page)).ToHaveTextAsync(["Jármű", "Termék", "Km", "Dátum", "Mennyiség", "EO"]);

        await page.Locator("button[aria-label='Feljebb: Dátum']").ClickAsync();
        await Expect(Names(page)).ToHaveTextAsync(["Jármű", "Termék", "Dátum", "Km", "Mennyiség", "EO"]);
        await page.Locator("button[aria-label='Lejjebb: Mennyiség']").ClickAsync();
        await Expect(Names(page)).ToHaveTextAsync(["Jármű", "Termék", "Dátum", "Km", "EO", "Mennyiség"]);
        await page.Locator("button[aria-label='Feljebb: Mennyiség']").ClickAsync();
        await Expect(Names(page)).ToHaveTextAsync(["Jármű", "Termék", "Dátum", "Km", "Mennyiség", "EO"]);
        await page.Locator("button[aria-label='Törlés: Km']").ClickAsync();

        await Expect(Names(page)).ToHaveTextAsync(["Jármű", "Termék", "Dátum", "Mennyiség", "EO"]);
        Assert.Empty(session.PageErrors);
    }

    private static ILocator Names(IPage page) => page.Locator("table tbody tr td:first-child");

    private static async Task Tick(IPage page, string name, int cell)
    {
        var box = page.Locator("table tbody tr", new() { Has = page.Locator("td:first-child", new() { HasTextString = name }) })
            .Locator("td").Nth(cell).Locator("input[type=checkbox]");
        await box.ClickAsync();
        await Expect(box).ToBeCheckedAsync();
    }
}
