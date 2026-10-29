using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A table that fits its box scrolls nothing (TA and Eszaf, 2026-10-29): a heading said to a screen reader only, at
///     the end of a column that keeps to its end, sat a pixel past the table's edge, and every list drew a scrollbar
///     under rows that fitted.
/// </summary>
public sealed class TableFitsItsBoxTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Theory]
    [InlineData(1280)]
    [InlineData(1440)]
    [InlineData(1013)]
    public async Task A_table_as_wide_as_its_box_has_nothing_to_scroll(int width)
    {
        await using var session = await HookSession.OpenAsync<ActionsColumnPage>(playwright);
        var page = session.Page;
        await page.SetViewportSizeAsync(width, 800);
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(3);

        var overflow = await page.Locator("[data-ui-table]").EvaluateAsync<double[]>(
            "t => [t.parentElement.scrollWidth - t.parentElement.clientWidth, t.parentElement.scrollHeight - t.parentElement.clientHeight]");

        Assert.Equal([0d, 0d], overflow);
        Assert.Empty(session.PageErrors);
    }
}

/// <summary>A list as the apps write it: the row's actions at its end, under a heading only a screen reader says.</summary>
public sealed partial class ActionsColumnPage : Component
{
    protected override Component? HeadAssets => [Markup.Title["a table that fits its box"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override Component? Render() =>
        Ui.Table.Class("w-full")[
            Ui.TableColumns[
                Ui.TableColumn.Class("w-12")[Span.Class("sr-only")["Kijelölés"]],
                Ui.TableColumn["Megnevezés"],
                Ui.TableColumn.End.Class("w-40")["Mennyiség"],
                Ui.TableColumn.End.Class("w-24")[Span.Class("sr-only")["Műveletek"]]
            ],
            Ui.TableRows[new[] { 1, 2, 3 }.Select(id => Ui.TableRow.Key(id)[
                Ui.TableCell[Ui.Checkbox],
                Ui.TableCell["Kút " + id],
                Ui.TableCell.End[(id * 10) + " l"],
                Ui.TableCell.End[Ui.Button.Ghost.Sm.Square(true).Icon(Ui.IconName.Pencil).Aria("label", "Módosítás")]
            ])]
        ];
}
