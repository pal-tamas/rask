using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     THE KEYBOARD RING on the Server host (owner, 2026-11-10): TA's list, wider than a phone, its ⋯ held in the table's
///     sticky last cell, drew Safari's blue ring there, cut by the table's scroll frame. Every control Tab reaches on
///     TA's shapes is ringed in the accent and nothing around it cuts the ring — at 1280 and 390, light and dark.
/// </summary>
public sealed class KeyboardRingTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Theory]
    [InlineData(1280, false)]
    [InlineData(1280, true)]
    [InlineData(390, false)]
    [InlineData(390, true)]
    public async Task Every_control_on_a_list_is_ringed_in_the_accent_and_the_tables_frame_cuts_none_of_it(int width, bool dark)
    {
        await using var session = await OpenAsync<KeyboardRingPage>(width, dark);

        var (faults, stops) = await KeyboardRing.SweepAsync(session.Page, "body");

        Assert.True(stops >= 12, $"only {stops} stops were walked");
        Assert.True(faults.Count == 0, string.Join('\n', faults));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task The_row_menus_ring_is_the_accent_inside_its_own_box_in_the_sticky_cell(int width)
    {
        await using var session = await OpenAsync<StickyActionsPage>(width, dark: false);
        var page = session.Page;
        var more = page.Locator("[data-ui-row-actions] [data-ui-button]").First;

        await session.TabToAsync("[data-ui-row-actions] [data-ui-button]");

        await Expect(more).ToBeFocusedAsync();
        await Expect(more).ToHaveCSSAsync("outline-style", "solid");
        await Expect(more).ToHaveCSSAsync("outline-width", "2px");
        await Expect(more).ToHaveCSSAsync("outline-offset", "-2px");
        Assert.Equal(await AccentAsync(page), await more.EvaluateAsync<string>("el => getComputedStyle(el).outlineColor"));
        var (faults, _) = await KeyboardRing.SweepAsync(page, "body");
        Assert.True(faults.Count == 0, string.Join('\n', faults));
    }

    [Fact]
    public async Task The_sticky_actions_stay_flush_with_the_frames_edge_on_a_phone()
    {
        await using var session = await OpenAsync<StickyActionsPage>(390, dark: false);
        var page = session.Page;

        var edges = await page.Locator("[data-ui-table]").EvaluateAsync<double[]>(
            "t => { const f = t.parentElement; f.scrollLeft = 80; const r = f.getBoundingClientRect(); const cell = t.querySelector('[data-ui-row-actions]').getBoundingClientRect();"
            + " return [r.left + f.clientLeft + f.clientWidth, cell.right, f.scrollWidth - f.clientWidth]; }");

        Assert.True(edges[2] > 0, "the list does not scroll sideways at 390");
        Assert.True(Math.Abs(edges[1] - edges[0]) <= 0.5, $"frame {edges[0]} cell {edges[1]}");
    }

    [Fact]
    public async Task A_press_on_the_row_menu_draws_no_ring()
    {
        await using var session = await OpenAsync<StickyActionsPage>(1280, dark: false);
        var page = session.Page;
        var first = page.Locator("[data-ui-row-actions] [data-ui-button]").First;

        await first.ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.Mouse.ClickAsync(1, 1);
        await first.ClickAsync();

        await Expect(first).ToHaveCSSAsync("outline-style", "none");
    }

    private static Task<string> AccentAsync(IPage page) => page.EvaluateAsync<string>(
        "() => { const i = document.body.appendChild(document.createElement('i')); i.style.color = 'var(--color-accent)'; const c = getComputedStyle(i).color; i.remove(); return c; }");

    private async Task<HookSession> OpenAsync<TPage>(int width, bool dark)
        where TPage : Component
    {
        var session = await HookSession.OpenAsync<TPage>(playwright, new BrowserNewContextOptions { ViewportSize = new() { Width = width, Height = 844 } });
        await session.HooksLoadedAsync();
        await KeyboardRing.SchemeAsync(session.Page, dark);
        await Expect(session.Page.Locator("tbody tr").First).ToBeVisibleAsync();
        return session;
    }
}

/// <summary>
///     TA's list and what stands round it: a first column that is a link and a box, the ⋯ in the sticky last cell, the
///     tabs over it, a primary and a ghost button, a switch, a list sorted by its first and last headings, and the pages
///     under it — each at a scroll frame's edge.
/// </summary>
public sealed partial class KeyboardRingPage : Component
{
    private const string AtTheEdge = "sticky right-0 z-10 edge-ground";

    protected override Component? HeadAssets =>
        [Markup.Title["every control of a list, by keyboard"], Markup.Style[Raw.Value(UiStylesheet.Css + ".min-w-160{min-width:40rem}.sticky{position:sticky}.right-0{right:0}.z-10{z-index:10}.edge-ground{background-color:#fff}.p-4{padding:1rem}.dark .edge-ground{background-color:#18181b}.dark body{background:#18181b}")]];

    protected override Component? Render() =>
        Div.Class("p-4")[
            Ui.Tabs.Pills.Key("tabs")[Ui.Tab.Key("all").Name("all")["Mind"], Ui.Tab.Key("open").Name("open")["Nyitott"], Ui.Tab.Key("closed").Name("closed")["Lezárt"]],
            Div.Class("p-4")[
                Ui.Button.Key("new").Primary["Új beszállító"],
                Ui.Button.Key("export").Ghost.Icon(Ui.IconName.ArrowDownTray).Tooltip("Export"),
                Ui.Switch.Value(false).Key("active").Label("Csak aktív")
            ],
            Ui.Table.Class("w-full min-w-160")[
                Ui.TableColumns[
                    Ui.TableColumn[Span.Class("sr-only")["Kijelölés"]],
                    Ui.TableColumn["Megnevezés"],
                    Ui.TableColumn["Adószám"],
                    Ui.TableColumn.End.Class(AtTheEdge)[Span.Class("sr-only")["Műveletek"]]
                ],
                Ui.TableRows[new[] { 1, 2, 3 }.Select(id => Ui.TableRow.Key(id)[
                    Ui.TableCell[Ui.Checkbox.Key("pick").Name("pick" + id)],
                    Ui.TableCell[Ui.Link.Key("name").Href(new Rask.Core.Routing.RouteUrl("/" + id))["Beszállító " + id]],
                    Ui.TableCell["1234567" + id],
                    Ui.RowActions.Key(id).Edit(new Rask.Core.Routing.RouteUrl("/" + id))
                ])]
            ],
            Ui.Table.Key("sorted")[
                Ui.TableColumns[Ui.TableColumn.Key("name").Sortable()["Név"], Ui.TableColumn.Key("sum").End.Sortable()["Összeg"]],
                Ui.TableRows[Ui.TableRow.Key("one")[Ui.TableCell["Beszállító 1"], Ui.TableCell.End["12 000"]]]
            ],
            Ui.Pagination.Paginator(new UiPaginator { Page = 1, PerPage = 20, Total = 60 }).Key("pages")
        ];
}
