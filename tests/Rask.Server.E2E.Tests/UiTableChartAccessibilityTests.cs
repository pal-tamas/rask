using System.Globalization;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The kit's table and chart on a page a session drives (the Server host), in a real browser, at a desktop's width
///     in the light and a phone's in the dark: the sorted heading's <c>aria-sort</c> follows a sort pressed from the
///     keyboard, a sticky column's scroll area is marked for the sides that hide columns, and a chart's drawing is an
///     image named and described by its rows (owner, 2026-11-10). The WASM site's twin is <c>UiKitAccessibilityTests</c>.
/// </summary>
public sealed class UiTableChartAccessibilityTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    public static TheoryData<int, int, string> Screens() => new() { { 1280, 720, "light" }, { 390, 844, "dark" } };

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task The_sorted_heading_says_which_way_and_moves_with_a_keyboard_sort(int width, int height, string scheme)
    {
        await using var session = await OpenAsync(width, height, scheme);
        var page = session.Page;
        var (name, amount) = (page.Locator("#by-name"), page.Locator("#by-amount"));

        await Expect(name).ToHaveAttributeAsync("aria-sort", "ascending");
        await amount.Locator("button").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(amount).ToHaveAttributeAsync("aria-sort", "ascending");
        await page.Keyboard.PressAsync("Enter");

        await Expect(amount).ToHaveAttributeAsync("aria-sort", "descending");
        Assert.Null(await name.GetAttributeAsync("aria-sort"));
        Assert.Equal(1, await page.Locator("#orders th[aria-sort]").CountAsync());
        Assert.Null(await page.Locator("#orders th").First.GetAttributeAsync("aria-sort"));
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task A_sticky_columns_scroll_area_is_marked_for_the_sides_that_hide_columns(int width, int height, string scheme)
    {
        await using var session = await OpenAsync(width, height, scheme);
        var page = session.Page;
        await session.HooksLoadedAsync();
        var area = page.Locator("#wide").Locator("xpath=..");
        var first = page.Locator("#wide tbody td").First;
        const string shadow = "el => getComputedStyle(el, '::after').boxShadow";

        await Expect(area).ToHaveAttributeAsync("data-rask-scroll-edges", "");
        await Expect(area).ToHaveAttributeAsync("data-scrolled-left", "");
        await Expect(area).Not.ToHaveAttributeAsync("data-scrolled-right", "");
        Assert.Equal("none", await first.EvaluateAsync<string>(shadow));
        await area.EvaluateAsync("el => el.scrollTo(40, 0)");
        await Expect(area).ToHaveAttributeAsync("data-scrolled-right", "");
        await Expect(area).ToHaveAttributeAsync("data-scrolled-left", "");
        await page.WaitForFunctionAsync("el => getComputedStyle(el, '::after').boxShadow !== 'none'", await first.ElementHandleAsync());
        await area.EvaluateAsync("el => el.scrollTo(el.scrollWidth, 0)");

        await Expect(area).ToHaveAttributeAsync("data-scrolled-right", "");
        await Expect(area).Not.ToHaveAttributeAsync("data-scrolled-left", "");
        // A render of the page keeps the marks the runtime wrote: the morph leaves them alone.
        await page.Locator("#by-name button").ClickAsync();
        await Expect(page.Locator("#by-name")).ToHaveAttributeAsync("aria-sort", "descending");
        await Expect(area).ToHaveAttributeAsync("data-scrolled-right", "");
        // The box made wide enough for every column: nothing is hidden, and the shadow goes.
        await area.EvaluateAsync("el => { el.parentElement.style.maxWidth = 'none'; el.parentElement.style.width = '6000px'; }");
        await Expect(area).Not.ToHaveAttributeAsync("data-scrolled-right", "");
        await Expect(area).Not.ToHaveAttributeAsync("data-scrolled-left", "");
        Assert.Equal("none", await first.EvaluateAsync<string>(shadow));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task A_charts_drawing_is_an_image_named_and_described_by_its_rows(int width, int height, string scheme)
    {
        await using var session = await OpenAsync(width, height, scheme);
        var page = session.Page;
        var svg = page.Locator("#visits svg[role='img']");

        await Expect(svg).ToHaveAttributeAsync("aria-label", "Line chart: Visitors. 4 data points, Mon to Thu. Values from 3 to 9.", new() { Timeout = 15_000 });
        var table = page.Locator("table#" + await svg.GetAttributeAsync("aria-describedby"));
        await Expect(table.Locator("thead th")).ToHaveTextAsync(["Label", "Visitors"]);
        await Expect(table.Locator("tbody tr")).ToHaveCountAsync(4);
        var box = (await table.Locator("xpath=..").BoundingBoxAsync())!;

        Assert.True(box.Width <= 1 && box.Height <= 1, $"the rows' table shows: {box.Width}x{box.Height}.");
        Assert.Equal("Tue 9", string.Join(' ', await table.Locator("tbody tr").Nth(1).Locator("th, td").AllTextContentsAsync()));
    }

    private Task<HookSession> OpenAsync(int width, int height, string scheme) =>
        HookSession.OpenAsync<TableChartAccessibilityPage>(playwright, new BrowserNewContextOptions
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
        });
}

/// <summary>A sortable table, a wide table with a sticky first column, and a line chart with its tooltip.</summary>
public sealed partial class TableChartAccessibilityPage : Component
{
    private static readonly (string Name, int Amount)[] Orders = [("Ada", 30), ("Bob", 10), ("Cyd", 20)];

    private static readonly Visit[] Visits = [new("Mon", 3), new("Tue", 9), new("Wed", 5), new("Thu", 7)];

    private string _sortBy = "name";
    private Ui.TableColumnDirection _direction = Ui.TableColumnDirection.Asc;

    protected override Component? HeadAssets => [Markup.Title["table and chart"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Ui.Table.Id("orders")[
            Ui.TableColumns[
                Ui.TableColumn["#"],
                Column("name", "Name"),
                Column("amount", "Amount")
            ],
            Ui.TableRows[Sorted().Select((order, i) => Ui.TableRow.Key(order.Name)[
                Ui.TableCell[(i + 1).ToString(CultureInfo.InvariantCulture)], Ui.TableCell[order.Name], Ui.TableCell[order.Amount.ToString(CultureInfo.InvariantCulture)]])]
        ],
        Div.Style("max-width: 320px")[Ui.Table.Id("wide")[
            Ui.TableColumns[
                Ui.TableColumn.Sticky().Class("bg-white dark:bg-zinc-900")["ID"],
                Enumerable.Range(1, 8).Select(i => Ui.TableColumn.Key(i)["Column " + i.ToString(CultureInfo.InvariantCulture)])
            ],
            Ui.TableRows[Enumerable.Range(1, 3).Select(row => Ui.TableRow.Key(row)[
                Ui.TableCell.Sticky().Class("bg-white dark:bg-zinc-900")[row.ToString(CultureInfo.InvariantCulture)],
                Enumerable.Range(1, 8).Select(i => Ui.TableCell.Key(i)["A long value " + i.ToString(CultureInfo.InvariantCulture)])
            ])]
        ]],
        Div.Id("visits").Style("width: 320px")[
            Ui.Chart.Value(Visits).Class("aspect-3/1")[
                Ui.ChartSvg[Ui.ChartLine.Field((Visit v) => v.Visitors), Ui.ChartAxis.X.Field((Visit v) => v.Day)],
                Ui.ChartTooltip[Ui.ChartTooltipValue.Field((Visit v) => v.Visitors).Label("Visitors")]
            ]
        ]
    ];

    private Component Column(string key, string label) =>
        Ui.TableColumn.Key(key).Id("by-" + key).Sortable().Sorted(_sortBy == key).Direction(_direction).OnSort(() => SortBy(key))[label];

    private void SortBy(string key)
    {
        _direction = _sortBy == key && _direction == Ui.TableColumnDirection.Asc ? Ui.TableColumnDirection.Desc : Ui.TableColumnDirection.Asc;
        _sortBy = key;
    }

    private IEnumerable<(string Name, int Amount)> Sorted()
    {
        var sorted = _sortBy == "amount" ? Orders.OrderBy(order => order.Amount) : Orders.OrderBy(order => order.Name, StringComparer.Ordinal);
        return _direction == Ui.TableColumnDirection.Asc ? sorted : sorted.Reverse();
    }

    private sealed record Visit(string Day, int Visitors);
}
