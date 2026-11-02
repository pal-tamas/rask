using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A row's checkbox in a kit table takes a real click (TA's stock list, nightly .2537): with every cell made
///     <c>relative</c>, the cell after a narrow checkbox column painted over the box and took the click instead.
/// </summary>
public sealed class TableCheckboxClickTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_rows_checkbox_and_the_headings_take_a_real_click()
    {
        await using var session = await HookSession.OpenAsync<StockListPage>(playwright);
        var page = session.Page;
        await page.SetViewportSizeAsync(1440, 1000);
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(3);

        await Box(page.Locator("tbody tr").Nth(1)).ClickAsync();
        await Expect(page.Locator("[data-rask-key=count]")).ToHaveTextAsync("1 kijelölve");
        await Box(page.Locator("thead")).ClickAsync();

        await Expect(page.Locator("[data-rask-key=count]")).ToHaveTextAsync("3 kijelölve");
        await Expect(page.Locator("tbody input[type=checkbox]:checked")).ToHaveCountAsync(3);
        Assert.Empty(session.PageErrors);
    }

    // A `table-fixed` table gives a column with no width what the others leave, and TA's leave nothing: the ticking
    // column shrank to its padding and the box reached 6px over the date. That is the table's own layout, as Flux's:
    // the column takes a width that holds its box and badges, as every column but one does.
    [Fact]
    public async Task A_ticking_column_given_a_width_draws_each_box_clear_of_the_next_cells_text()
    {
        await using var session = await HookSession.OpenAsync<StockListPage>(playwright);
        var page = session.Page;
        await page.SetViewportSizeAsync(1440, 1000);
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(3);
        await page.Locator("thead th").First.EvaluateAsync("th => th.style.width = '8rem'");

        var gaps = await page.Locator("[data-ui-table]").EvaluateAsync<double[]>(
            """
            t => [...t.querySelectorAll('tr')].map(row => {
              const box = row.querySelector('[data-ui-checkbox]').getBoundingClientRect();
              const range = document.createRange();
              range.selectNodeContents(row.children[1]);
              return range.getBoundingClientRect().left - box.right;
            })
            """);

        Assert.All(gaps, gap => Assert.True(gap >= 0, "A checkbox reached over the next cell's text by " + -gap + "px."));
        Assert.Empty(session.PageErrors);
    }

    private static ILocator Box(ILocator within) => within.Locator("[data-ui-checkbox]").First;
}

/// <summary>
///     TA's stock list as it writes it: a ticking column of its own with no width, badges beside the box, and fixed widths
///     elsewhere that leave it nothing (TA writes them as <c>w-40</c>, <c>w-60</c>, <c>w-24</c>, <c>w-full min-w-160</c>,
///     which only an app's Tailwind build compiles, so they are stated here as styles).
/// </summary>
public sealed partial class StockListPage : Component
{
    private static readonly int[] Ids = [1, 2, 3];

    private readonly HashSet<int> _chosen = [];

    protected override Component? HeadAssets => [Markup.Title["a checkbox in a table"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "hu";

    protected override Component? Render() =>
    [
        Ui.Text.Key("count")[$"{_chosen.Count} kijelölve"],
        Div.Style("width: 1120px")[
            Ui.Table.Style("width: 100%; min-width: 40rem")[
                Ui.TableColumns[
                    Ui.TableColumn.Key("picks")[Ui.Checkbox.Key("all").Checked(_chosen.Count == Ids.Length).OnChange(on => All(on))],
                    Ui.TableColumn.Key("moment").Style("width: 10rem")["Teljesítés dátuma"],
                    Ui.TableColumn.Key("invoice").Style("width: 10rem")["Számla"],
                    Ui.TableColumn.Key("driver").Style("width: 10rem")["Sofőr"],
                    Ui.TableColumn.Key("vehicle").Style("width: 15rem")["Jármű"],
                    Ui.TableColumn.Key("in").Style("width: 10rem").End["Mennyiség be"],
                    Ui.TableColumn.Key("out").Style("width: 10rem").End["Mennyiség ki"],
                    Ui.TableColumn.Key("row-actions").End.Style("width: 6rem")[Span.Class("sr-only")["Műveletek"]]
                ],
                Ui.TableRows[Ids.Select(id => Ui.TableRow.Key(id)[
                    Ui.TableCell.Key("pick-" + id)[Div.Class("flex items-center gap-2")[
                        Ui.Checkbox.Key("box-" + id).Checked(_chosen.Contains(id)).OnChange(on => Choose(id, on)),
                        id == 2 ? Span.Key("by-hand-" + id).Title("Kézzel rögzített tranzakció")[Ui.Badge.Sm["Kézi"]] : null
                    ]],
                    Ui.TableCell[Div["2026. 09. 2" + id + "."], Div["10:25"]],
                    Ui.TableCell["90322468" + id],
                    Ui.TableCell[Div["Karsai Bence"], Div["01050C0C01000000"]],
                    Ui.TableCell[Div["00303"], Div["Munkagépek"]],
                    Ui.TableCell.End,
                    Ui.TableCell.End[(id * 10) + ",00"],
                    Ui.TableCell.Key("row-actions-" + id).End.Class("py-0")[Ui.Button.Ghost.Sm.Square(true).Icon(Ui.IconName.Pencil).Aria("label", "Módosítás")]
                ])]
            ]
        ]
    ];

    private void All(bool on)
    {
        if (on)
        {
            _chosen.UnionWith(Ids);
        }
        else
        {
            _chosen.Clear();
        }
    }

    private void Choose(int id, bool on)
    {
        if (on)
        {
            _chosen.Add(id);
        }
        else
        {
            _chosen.Remove(id);
        }
    }
}
