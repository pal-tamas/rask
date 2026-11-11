using System.Globalization;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     TA's list header, as production drew it: the Filters button beside an Export dropdown and a toggle, a badge of
///     forty vehicles, a badge of one long route — and the list under it, a sortable column of numbers kept at the end.
///     No badge widens the page, the three buttons are one height, and the heading of numbers stands over them.
/// </summary>
public sealed class AListHeaderHoldsItsFiltersTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string RouteBadge = "[data-ui-filters-badges] [data-ui-badge][title]";

    private const string VehiclesTooltip = "[data-ui-filters-badges] > [data-ui-tooltip]";

    private Task<HookSession> OpenAsync(int width) =>
        HookSession.OpenAsync<ListHeaderPage>(playwright, new() { ViewportSize = new() { Width = width, Height = 800 }, HasTouch = width < 640 });

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task A_long_badge_ends_in_an_ellipsis_never_widens_the_page_and_its_cross_still_takes_it_off(int width)
    {
        await using var session = await OpenAsync(width);
        var page = session.Page;
        await Expect(page.Locator(RouteBadge)).ToHaveCountAsync(1);

        var overflow = await page.EvaluateAsync<double>("() => document.documentElement.scrollWidth - innerWidth");
        var cut = await page.Locator(RouteBadge + " > span").EvaluateAsync<bool>("s => s.scrollWidth > s.clientWidth");
        var title = await page.Locator(RouteBadge).GetAttributeAsync("title");
        var crossOnTop = await page.Locator(RouteBadge + " > [data-ui-badge-close]").EvaluateAsync<bool>(
            "b => { const r = b.getBoundingClientRect(); const hit = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2); return r.width > 0 && r.right <= innerWidth && b.contains(hit); }");

        Assert.True(overflow <= 0, $"the page scrolls {overflow}px sideways");
        Assert.True(cut, "the route's words are not cut off");
        Assert.Equal("Útvonal: " + ListHeaderPage.Route, title);
        Assert.True(crossOnTop, "the cross is out of view or covered");

        await page.Locator(RouteBadge + " > [data-ui-badge-close]").ClickAsync();

        await Expect(page.Locator(RouteBadge)).ToHaveCountAsync(0);
        await Expect(page.Locator(VehiclesTooltip)).ToHaveCountAsync(1);
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task The_filters_button_the_export_button_and_the_toggle_are_one_height(int width)
    {
        await using var session = await OpenAsync(width);
        var page = session.Page;
        await Expect(page.Locator("[data-ui-filters] [data-ui-modal-trigger] button")).ToBeVisibleAsync();

        var heights = await page.EvaluateAsync<double[]>(
            "() => ['[data-ui-filters] [data-ui-modal-trigger] button', '#export', '#archived'].map(s => document.querySelector(s).getBoundingClientRect().height)");

        Assert.Equal([40d, 40d, 40d], heights);
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task Forty_vehicles_are_counted_on_the_badge_and_listed_in_a_tooltip_kept_on_the_screen(int width)
    {
        await using var session = await OpenAsync(width);
        var page = session.Page;
        var badge = page.Locator(VehiclesTooltip + " > [data-ui-badge]");
        var tooltip = page.Locator(VehiclesTooltip + " > [data-ui-tooltip-content]");
        await Expect(badge).ToHaveTextAsync("Járművek: 40 selected");

        if (width < 640)
        {
            await badge.Locator("span").TapAsync();
        }
        else
        {
            await badge.Locator("span").HoverAsync();
        }

        await Expect(tooltip).ToBeVisibleAsync();
        var box = await tooltip.EvaluateAsync<double[]>("t => { const r = t.getBoundingClientRect(); return [r.left, r.right, r.width, innerWidth, r.height]; }");

        await Expect(tooltip).ToHaveTextAsync(string.Join(", ", ListHeaderPage.Vehicles));
        Assert.Null(await badge.GetAttributeAsync("title"));
        Assert.True(box[0] >= 0 && box[1] <= box[3], $"the tooltip runs off the screen: {box[0]}..{box[1]} of {box[3]}");
        Assert.True(box[2] < box[3] - 20 && box[4] > 40, $"the tooltip is {box[2]} x {box[4]}px, not wrapped");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task The_vehicles_tooltip_opens_on_keyboard_focus()
    {
        await using var session = await OpenAsync(1280);
        var page = session.Page;
        await Expect(page.Locator(VehiclesTooltip)).ToHaveCountAsync(1);

        await session.TabToAsync(VehiclesTooltip + " [data-ui-badge-close]");

        await Expect(page.Locator(VehiclesTooltip + " > [data-ui-tooltip-content]")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task A_sortable_column_kept_at_the_end_stands_its_heading_over_its_numbers(int width)
    {
        await using var session = await OpenAsync(width);
        var page = session.Page;
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(3);

        var edges = await page.EvaluateAsync<double[][]>(
            """
            () => [2, 3].map(n => {
                const right = node => { const r = document.createRange(); r.selectNodeContents(node); return r.getBoundingClientRect().right; };
                const heading = document.querySelector(`thead th:nth-child(${n}) [data-ui-table-sortable] > div:first-child`);
                return [right(heading), ...[...document.querySelectorAll(`tbody td:nth-child(${n})`)].map(right)];
            })
            """);

        var parts = await page.EvaluateAsync<string>(
            """
            () => [...document.querySelectorAll('thead th:nth-child(2) *')].concat([document.querySelector('thead th:nth-child(2)')]).map(e => {
                const r = e.getBoundingClientRect(), s = getComputedStyle(e);
                return `${e.tagName}.${e.className}: ${r.left}..${r.right} pad ${s.paddingLeft}/${s.paddingRight} margin ${s.marginLeft}/${s.marginRight} jc ${s.justifyContent}`;
            }).join(' | ')
            """);

        Assert.All(edges, column => Assert.All(column.Skip(1), cell => Assert.True(Math.Abs(column[0] - cell) <= 1, $"heading ends at {column[0]}, its numbers at {cell}: {parts}")));
    }
}

/// <summary>TA's fuelings list: its header of filters, Export and a toggle, its badges, and a column of litres.</summary>
public sealed partial class ListHeaderPage : Component
{
    /// <summary>The forty vehicles the vehicles filter holds.</summary>
    internal static readonly string[] Vehicles =
        [.. Enumerable.Range(1, 40).Select(n => n.ToString("00000", CultureInfo.InvariantCulture) + " - Case III MXM" + (100 + n).ToString(CultureInfo.InvariantCulture))];

    /// <summary>A route longer than any row: one name, said whole.</summary>
    internal const string Route =
        "Budapest, Tatabánya, Győr, Mosonmagyaróvár, Hegyeshalom, Bruck an der Leitha, Schwechat, Wien, Sankt Pölten, Linz, "
        + "Salzburg, Rosenheim, München, Augsburg, Ulm, Stuttgart, Karlsruhe, Strasbourg, Nancy, Metz, Reims, Paris, "
        + "Orléans, Tours, Poitiers, Bordeaux, Toulouse, Montpellier, Marseille, Nice, Genova, Milano, Verona, Venezia, Trieste, Ljubljana, Zagreb";

    // What an app's own Tailwind build brings, which the kit's sheet leaves to it: a button with no border of the browser's.
    private const string Layout =
        "button{border:0}.ta-page{padding:1rem}.ta-header{display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:.5rem}"
        + ".ta-actions{display:flex;align-items:center;gap:.5rem}.ta-row{display:flex;margin-top:1rem}.w-32{width:8rem}";

    private List<string> _vehicles = [.. Vehicles];
    private string? _route = Route;

    protected override Component? HeadAssets => [Markup.Title["a list header of filters"], Markup.Style[Raw.Value(UiStylesheet.Css + Layout)]];

    protected override Component? Render()
    {
        var filters = Ui.Filters[
            Ui.Filter.Label("Járművek").Shown(_vehicles).OnRemove(() => { _vehicles = []; }),
            Ui.Filter.Label("Útvonal").Shown(_route).OnRemove(() => { _route = null; })
        ];

        return Div.Class("ta-page")[
            Div.Class("ta-header")[
                Ui.Heading["Tankolások"],
                Div.Class("ta-actions")[
                    filters,
                    Ui.Dropdown[
                        Ui.Button.Id("export").IconTrailing(Ui.IconName.ChevronDown)["Export"],
                        Ui.Menu[Ui.MenuItem["Excel"], Ui.MenuItem["CSV"]]
                    ],
                    Ui.Toggle.Id("archived").Icon(Ui.IconName.ArchiveBox).Tooltip("Archivált")
                ]
            ],
            Div.Class("ta-row")[filters.Badges],
            Ui.Table[
                Ui.TableColumns[
                    Ui.TableColumn["Rendszám"],
                    Ui.TableColumn.End.Sortable().Class("w-32")["Liter"],
                    Ui.TableColumn.End.Sortable().Sorted().Class("w-32")["Összeg"]
                ],
                Ui.TableRows[new[] { 1, 2, 3 }.Select(id => Ui.TableRow.Key(id)[
                    Ui.TableCell[Vehicles[id]],
                    Ui.TableCell.End[(id * 41.5).ToString("0.0", CultureInfo.InvariantCulture)],
                    Ui.TableCell.End[(id * 25_300).ToString("N0", CultureInfo.InvariantCulture)]
                ])]
            ]
        ];
    }
}
