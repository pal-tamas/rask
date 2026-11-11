using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     TA's list on the Server host: the ⋯ of <c>Ui.RowActions</c> wears the tooltip "Actions", and its menu hands focus
///     back to it when it closes. That focus is no keyboard navigation, so the tooltip stays shut; a Tab onto the ⋯ shows
///     it, inside the viewport, over the scroller and the sticky cell it sits in.
/// </summary>
public sealed class RowActionsTooltipTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Trigger = "tbody tr:first-child [data-ui-row-actions] [data-ui-tooltip] > button";

    [Theory]
    [MemberData(nameof(TooltipWalk.Closes), MemberType = typeof(TooltipWalk))]
    public async Task A_menu_the_mouse_closed_hands_focus_back_to_its_button_without_its_tooltip(int width, string close)
    {
        await using var session = await HookSession.OpenAsync<RowActionsTooltipPage>(playwright);
        var page = session.Page;
        await page.SetViewportSizeAsync(width, 800);
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(3);
        await session.HooksLoadedAsync();
        var row = page.Locator("tbody tr").First;
        var menu = row.Locator("[role=menu]");

        await TooltipWalk.CloseByMouseShowsNoTooltipAsync(page, page.Locator(Trigger), menu, menu.Locator("[role=menuitem]").First, page.Locator("#edited"), close);

        if (close == "pick")
        {
            await Expect(page.Locator("#edited")).ToHaveTextAsync("Edited 1");
        }

        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task A_Tab_onto_the_button_shows_its_tooltip_inside_the_viewport_over_the_scroller(int width)
    {
        await using var session = await HookSession.OpenAsync<RowActionsTooltipPage>(playwright);
        var page = session.Page;
        await page.SetViewportSizeAsync(width, 800);
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(3);
        await session.HooksLoadedAsync();

        await TooltipWalk.TabShowsTooltipInViewAsync(page, page.Locator(Trigger));

        Assert.Empty(session.PageErrors);
    }
}

/// <summary>TA's list (<see cref="StickyActionsPage" />), its ⋯ editing in place so a pick stays on the page.</summary>
public sealed partial class RowActionsTooltipPage : Component
{
    private const string AtTheEdge = "sticky right-0 z-10 bg-white";
    private int _edited;

    protected override Component? HeadAssets =>
        [Markup.Title["a list whose actions wear a tooltip"], Markup.Style[Raw.Value(UiStylesheet.Css + ".min-w-160{min-width:40rem}.sticky{position:sticky}.right-0{right:0}.z-10{z-index:10}.bg-white{background-color:#fff}.p-4{padding:1rem}")]];

    protected override Component? Render() =>
        Div.Class("p-4")[
            P.Id("edited")["Edited " + _edited],
            Ui.Table.Class("w-full min-w-160")[
                Ui.TableColumns[
                    Ui.TableColumn["Megnevezés"],
                    Ui.TableColumn["Adószám"],
                    Ui.TableColumn.End.Class(AtTheEdge)[Span.Class("sr-only")["Műveletek"]]
                ],
                Ui.TableRows[new[] { 1, 2, 3 }.Select(id => Ui.TableRow.Key(id)[
                    Ui.TableCell["Beszállító " + id],
                    Ui.TableCell["1234567" + id],
                    Ui.RowActions.Key(id).OnEdit(() => _edited++)
                ])]
            ]
        ];
}
