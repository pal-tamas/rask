using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A wide list of twelve columns whose headings are long labels: every heading's words stay inside its own column,
///     short of the next heading, on a desk and on a phone, with the columns sized by the kit's widths or left to the
///     browser.
/// </summary>
/// <remarks>
///     An app's own suite measured this after a long compound word ran over into its neighbour's heading. The words
///     are measured as the browser laid them out (a range over each heading's text), not by the heading's box, which
///     holds them whether they fit or not.
/// </remarks>
public sealed class LongHeadingsTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    // For every heading but the last, how far its text runs past the next heading's left edge (≤ 0 when it does not),
    // with what it says, so a failure names the heading.
    private const string Overruns =
        """
        () => {
            const ths = [...document.querySelectorAll('#long thead th')];
            const textRight = th => {
                let right = -Infinity;
                const walk = document.createTreeWalker(th, NodeFilter.SHOW_TEXT);
                for (let node = walk.nextNode(); node; node = walk.nextNode()) {
                    if (!node.textContent.trim()) continue;
                    const range = document.createRange();
                    range.selectNodeContents(node);
                    for (const rect of range.getClientRects()) right = Math.max(right, rect.right);
                }
                return right;
            };
            return ths.slice(0, -1).map((th, i) => `${(textRight(th) - ths[i + 1].getBoundingClientRect().left).toFixed(1)} ${th.textContent.trim()}`);
        }
        """;

    [Theory]
    [InlineData(1280, "widths")]
    [InlineData(390, "widths")]
    [InlineData(1280, "browser")]
    [InlineData(390, "browser")]
    public async Task No_long_heading_of_a_twelve_column_table_runs_into_the_next_one(int width, string sized)
    {
        LongHeadingsPage.Sized = sized;
        await using var session = await HookSession.OpenAsync<LongHeadingsPage>(
            playwright, new() { ViewportSize = new() { Width = width, Height = 800 } });
        var page = session.Page;
        await Expect(page.Locator("#long thead th")).ToHaveCountAsync(12);

        var overruns = await page.EvaluateAsync<string[]>(Overruns);

        var over = overruns.Where(o => double.Parse(o.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture) > 0.5).ToArray();
        Assert.True(over.Length == 0, "a heading's words run into the next heading: " + string.Join(" | ", over));
        Assert.Empty(session.PageErrors);
    }
}

/// <summary>A list of twelve columns, its headings long labels and long compound words, as a fleet report's are.</summary>
public sealed partial class LongHeadingsPage : Component
{
    private static volatile string _sized = "widths";

    private static readonly (string Label, Ui.ColumnWidth Width)[] Columns =
    [
        ("Registered supplier name", Ui.ColumnWidth.Name),
        ("Delivery reference number", Ui.ColumnWidth.Code),
        ("Fuel card identifier", Ui.ColumnWidth.Short),
        ("Day of the delivery", Ui.ColumnWidth.Day),
        ("Moment of the last reading", Ui.ColumnWidth.Moment),
        ("Quantity delivered in litres", Ui.ColumnWidth.Number),
        ("Netamountbeforetaxes", Ui.ColumnWidth.Number),
        ("Supplier's home town", Ui.ColumnWidth.Reference),
        ("Accepted by the warehouse", Ui.ColumnWidth.Flag),
        ("Registration plate of the truck", Ui.ColumnWidth.Short),
        ("Driver who signed the delivery", Ui.ColumnWidth.Reference),
        ("Remarks", Ui.ColumnWidth.Rest),
    ];

    /// <summary>"widths" to give every column a kit width, "browser" to leave them all to the browser.</summary>
    public static string Sized
    {
        get => _sized;
        set => _sized = value;
    }

    protected override Component? HeadAssets => [Markup.Title["long headings"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Main.Class("p-4")[
        Ui.Table.Id("long")[
            Ui.TableColumns[Columns.Select(c => Sized == "widths" ? Ui.TableColumn.Key(c.Label).Width(c.Width)[c.Label] : Ui.TableColumn.Key(c.Label)[c.Label])],
            Ui.TableRows[new[] { 1, 2 }.Select(id => Ui.TableRow.Key(id)[Columns.Select(c => Ui.TableCell.Key(c.Label)[c.Label[..3] + id])])]
        ]
    ];
}
