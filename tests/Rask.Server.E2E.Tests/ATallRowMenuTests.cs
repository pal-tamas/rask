using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     TA's pumps on a Server page: every row's ⋯ opens eighteen actions in three groups, taller than a phone. Opened from a
///     row at the top, in the middle and at the bottom of the viewport, the whole menu is reachable — its box inside the
///     viewport, held to the room there and scrolling inside, its first row in view as it opens, and every row the arrow
///     keys reach scrolled into view.
/// </summary>
public sealed class ATallRowMenuTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Theory]
    [InlineData(390, 600, TallMenus.Top)]
    [InlineData(390, 600, TallMenus.Middle)]
    [InlineData(390, 600, TallMenus.Bottom)]
    [InlineData(1280, 800, TallMenus.Top)]
    [InlineData(1280, 800, TallMenus.Middle)]
    [InlineData(1280, 800, TallMenus.Bottom)]
    public async Task A_tall_row_menu_opened_by_a_press_fits_the_viewport_and_scrolls_to_its_last_row(int width, int height, string where)
    {
        await using var session = await OpenAsync(width, height);
        var page = session.Page;
        var trigger = await TallMenus.TriggerAsync(page.Locator("tbody tr"), where);

        await trigger.ClickAsync();
        var opened = await TallMenus.OpenedAsync(page);
        var reach = await TallMenus.ScrolledThroughAsync(page);

        TallMenus.AssertFits(opened, width, height);
        Assert.Equal(0d, opened.ScrollTop);
        Assert.True(opened.FirstInside, $"the first row is not in the menu's box as it opens: {opened}");
        Assert.Equal(TallMenus.Rows, reach.Count);
        Assert.All(reach, inside => Assert.True(inside, $"a row scrolled into view is not in the menu's box: {string.Join(",", reach)}"));
    }

    [Theory]
    [InlineData(390, 600, TallMenus.Top)]
    [InlineData(390, 600, TallMenus.Middle)]
    [InlineData(390, 600, TallMenus.Bottom)]
    [InlineData(1280, 800, TallMenus.Top)]
    [InlineData(1280, 800, TallMenus.Middle)]
    [InlineData(1280, 800, TallMenus.Bottom)]
    public async Task A_tall_row_menu_opened_by_the_keyboard_focuses_its_first_row_and_the_arrows_keep_each_row_in_view(int width, int height, string where)
    {
        await using var session = await OpenAsync(width, height);
        var page = session.Page;
        var trigger = await TallMenus.TriggerAsync(page.Locator("tbody tr"), where);

        // Focus from the keyboard shows the ⋯'s tooltip, which marks the button expanded: the menu still opens.
        await trigger.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        var opened = await TallMenus.OpenedAsync(page);
        var walk = await TallMenus.WalkedAsync(page);

        TallMenus.AssertFits(opened, width, height);
        Assert.True(opened.FirstFocused, $"the first row does not have focus: {opened}");
        Assert.True(opened.FirstInside, $"the first row is not in the menu's box as it opens: {opened}");
        Assert.Equal(2 * TallMenus.Rows - 1, walk.Count);
        Assert.All(walk, step => Assert.True(step.Inside, $"a focused row is out of the menu's box: {string.Join(" | ", walk)}"));
        Assert.Equal(TallMenus.Rows, walk.Select(step => step.Key).Distinct().Count());
    }

    [Theory]
    [InlineData(390, 600)]
    [InlineData(390, 844)]
    [InlineData(1280, 800)]
    public async Task A_tall_row_menu_on_a_page_with_no_reset_keeps_its_padding_and_border_inside_the_viewport_too(int width, int height)
    {
        await using var session = await OpenAsync(width, height, reset: false);
        var page = session.Page;
        var trigger = await TallMenus.TriggerAsync(page.Locator("tbody tr"), TallMenus.Top);

        await trigger.ClickAsync();
        var opened = await TallMenus.OpenedAsync(page);

        Assert.Equal("content-box", await page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('[role=menu]:popover-open')).boxSizing"));
        Assert.True(opened.Capped, $"the menu is not held to a height: {opened}");
        TallMenus.AssertFits(opened, width, height);
        Assert.Equal(height - 5d, Math.Round(opened.Y + opened.Height));
    }

    private async Task<HookSession> OpenAsync(int width, int height, bool reset = true)
    {
        var session = await HookSession.OpenAsync<TallRowMenuPage>(playwright, new() { ViewportSize = new() { Width = width, Height = height } });
        await session.HooksLoadedAsync();
        if (reset)
        {
            // An app's own Tailwind brings its reset; this page has the kit's sheet alone, which carries it for a
            // document that says so.
            await session.Page.EvaluateAsync("() => document.documentElement.setAttribute('data-rask-ui-document', '')");
        }

        await Expect(session.Page.Locator("tbody tr")).ToHaveCountAsync(TallRowMenuPage.Count);
        return session;
    }
}

/// <summary>TA's pumps: a list whose every row has a ⋯ of eighteen actions in three groups.</summary>
public sealed partial class TallRowMenuPage : Component
{
    internal const int Count = 24;

    protected override Component? HeadAssets =>
        [Markup.Title["a list with tall row menus"], Markup.Style[Raw.Value(UiStylesheet.Css + ".p-4{padding:1rem}.h-12{height:3rem}")]];

    protected override Component? Render() =>
        Div.Class("p-4")[
            Ui.Table.Class("w-full")[
                Ui.TableColumns[
                    Ui.TableColumn["Megnevezés"],
                    Ui.TableColumn.RowMenu
                ],
                Ui.TableRows[Enumerable.Range(1, Count).Select(id => Ui.TableRow.Key(id)[
                    Ui.TableCell[Div.Class("h-12")["Kút " + id]],
                    Ui.RowActions.Key(id).View(new Rask.Core.Routing.RouteUrl("/v/" + id)).Edit(new Rask.Core.Routing.RouteUrl("/" + id))
                        .Remove(() => { }, "Kút törlése")[
                            Ui.MenuGroup.Key("ask").Heading("Letöltés")[Enumerable.Range(1, 10).Select(i => Ui.MenuItem.Key("ask" + i)["Lekérdezés " + i])],
                            Ui.MenuGroup.Key("tell").Heading("Feltöltés")[Enumerable.Range(1, 4).Select(i => Ui.MenuItem.Key("tell" + i)["Feltöltés " + i])],
                            Ui.MenuGroup.Key("stopping")[Ui.MenuItem.Key("stop")["Megszakítás"]]
                        ]
                ])]
            ]
        ];
}
