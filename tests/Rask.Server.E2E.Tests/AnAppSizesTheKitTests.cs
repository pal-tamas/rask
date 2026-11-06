using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     What the TA migration asked of the kit's sizes (#1283): a time picker is as wide as the class an app gives it, and
///     a heading said to a screen reader only scrolls nothing — no second scrollbar, sideways or down, under a list that
///     fits, nor under one a phone scrolls sideways with its actions held at the edge.
/// </summary>
public sealed class AnAppSizesTheKitTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task A_time_picker_is_as_wide_as_the_class_an_app_gives_it(int width)
    {
        await using var session = await HookSession.OpenAsync<SizedTimePickersPage>(playwright);
        var page = session.Page;
        await page.SetViewportSizeAsync(width, 800);
        await Expect(page.Locator("[data-ui-time-picker]")).ToHaveCountAsync(2);

        var widths = await page.Locator("[data-ui-time-picker]").EvaluateAllAsync<double[]>("pickers => pickers.map(p => p.getBoundingClientRect().width)");

        Assert.Equal([112d, 112d], widths);
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task A_heading_only_a_screen_reader_says_adds_no_scrollbar_to_a_list_its_actions_hold_at_the_edge(int width)
    {
        await using var session = await HookSession.OpenAsync<StickyActionsPage>(playwright);
        var page = session.Page;
        await page.SetViewportSizeAsync(width, 800);
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(3);

        var down = await page.Locator("[data-ui-table]").EvaluateAsync<double>("t => t.parentElement.scrollHeight - t.parentElement.clientHeight");
        var scrolls = await page.EvaluateAsync<double[]>("() => [document.documentElement.scrollWidth - innerWidth, document.documentElement.scrollHeight - innerHeight]");

        Assert.Equal(0d, down);
        Assert.True(scrolls[0] <= 0, $"the page scrolls {scrolls[0]}px sideways");
        Assert.True(scrolls[1] <= 0, $"the page scrolls {scrolls[1]}px down");
        Assert.Empty(session.PageErrors);
    }
}

/// <summary>Both of a time picker's triggers, each given the width TA's day-and-time pair gave its own (<c>w-28</c>).</summary>
public sealed partial class SizedTimePickersPage : Component
{
    protected override Component? HeadAssets => [Markup.Title["time pickers an app sizes"], Markup.Style[Raw.Value(UiStylesheet.Css + ".w-28{width:7rem}.p-4{padding:1rem}")]];

    protected override Component? Render() =>
        Div.Class("flex flex-col gap-4 p-4")[
            Ui.TimePicker.Key("button").Value(new TimeOnly(9, 30)).Class("w-28"),
            Ui.TimePicker.Key("input").Value(new TimeOnly(9, 30)).Type(Ui.TimePickerType.Input).Class("w-28")
        ];
}

/// <summary>
///     TA's list: wider than a phone, so it scrolls sideways there, its ⋯ held at the end (the kit's sticky cell, on the
///     page's surface), under a heading only a screen reader says.
/// </summary>
public sealed partial class StickyActionsPage : Component
{
    private const string AtTheEdge = "sticky right-0 z-10 bg-white";

    protected override Component? HeadAssets =>
        [Markup.Title["a list that holds its actions at the edge"], Markup.Style[Raw.Value(UiStylesheet.Css + ".min-w-160{min-width:40rem}.sticky{position:sticky}.right-0{right:0}.z-10{z-index:10}.bg-white{background-color:#fff}.p-4{padding:1rem}")]];

    protected override Component? Render() =>
        Div.Class("p-4")[
            Ui.Table.Class("w-full min-w-160")[
                Ui.TableColumns[
                    Ui.TableColumn["Megnevezés"],
                    Ui.TableColumn["Adószám"],
                    Ui.TableColumn.End.Class(AtTheEdge)[Span.Class("sr-only")["Műveletek"]]
                ],
                Ui.TableRows[new[] { 1, 2, 3 }.Select(id => Ui.TableRow.Key(id)[
                    Ui.TableCell["Beszállító " + id],
                    Ui.TableCell["1234567" + id],
                    Ui.RowActions.Key(id).Edit(new Rask.Core.Routing.RouteUrl("/" + id))
                ])]
            ]
        ];
}
