using Rask.Core;
using Rask.Core.Tests.Live;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.UiTests.Live;

// TA, first seen on .246: a keyed bar of actions above a kit table, whose count text comes while a row is ticked
// and goes when none is. Ticking a row shifted the column headings' texts onto the wrong columns, and unticking
// shifted them again. Every render is diffed and REPLAYED onto the page the browser holds (LiveBrowser).
//
// Already right on this tree, and kept as the pin for the reported shape. TableHeadingsBesideNewSiblingsTests pins the
// tagless-element render cache fix the closest earlier report needed.
public partial class KeyedSiblingAboveTableTests : global::Rask.Core.RaskMarkup
{
    private static readonly string[] Headings = ["Dátum", "Mennyiség", "Sofőr"];

    [Fact]
    public async Task Ticking_a_row_adds_only_the_count_and_leaves_the_column_headings_on_their_columns()
    {
        var page = new Transfers();
        var browser = LiveBrowser.Open(page);

        await browser.Change(Box(browser, 1), "true");

        Assert.Equal(Headings, ColumnTexts(browser));
        Assert.Equal("1 sor kijelölve", Count(browser)?.Children[0].Text);
        Assert.Equal(["false", "true", "false"], Ticks(browser));
    }

    [Fact]
    public async Task Unticking_the_last_row_takes_only_the_count_away_and_the_headings_stay_put()
    {
        var page = new Transfers();
        var browser = LiveBrowser.Open(page);

        await browser.Change(Box(browser, 0), "true");
        await browser.Change(Box(browser, 2), "true");
        await browser.Change(Box(browser, 0), "false");
        await browser.Change(Box(browser, 2), "false");

        Assert.Equal(Headings, ColumnTexts(browser));
        Assert.Null(Count(browser));
        Assert.Equal(["false", "false", "false"], Ticks(browser));
    }

    [Fact]
    public async Task Clearing_the_choice_from_the_bar_takes_the_count_away_and_the_headings_stay_put()
    {
        var page = new Transfers();
        var browser = LiveBrowser.Open(page);

        await browser.Change(Box(browser, 0), "true");
        await browser.Change(Box(browser, 1), "true");
        await browser.Click(LiveBrowser.Find(browser.Page.At(), static node => node.Key == "none")!);

        Assert.Equal(Headings, ColumnTexts(browser));
        Assert.Null(Count(browser));
        Assert.Equal("Áthelyezés (0)", Texts(LiveBrowser.Find(browser.Page.At(), static node => node.Key == "move")!).Single());
    }

    private static FrameDom.Node Box(LiveBrowser browser, int row) =>
        LiveBrowser.Find(browser.Rows()[row], static node => node.Tag == "input")!;

    private static FrameDom.Node? Count(LiveBrowser browser) => LiveBrowser.Find(browser.Page.At(), static node => node.Key == "count");

    private static string[] ColumnTexts(LiveBrowser browser) =>
        [.. LiveBrowser.Find(browser.Page.At(), static node => node.Tag == "thead")!.Children[0].Children.SelectMany(Texts)];

    private static string[] Ticks(LiveBrowser browser) =>
        [.. browser.Rows().Select(static row => Box(row).Attributes.ContainsKey("checked") ? "true" : "false")];

    private static FrameDom.Node Box(FrameDom.Node row) => LiveBrowser.Find(row, static node => node.Tag == "input")!;

    private static IEnumerable<string> Texts(FrameDom.Node node) =>
        node.Tag is null ? [node.Text!] : node.Children.SelectMany(Texts);

    private sealed class Transfers : Component
    {
        private readonly HashSet<int> _chosen = [];

        protected override Component? Render() =>
        [
            Div.Key("actions")[
                Ui.Button.Key("move").Disabled(_chosen.Count == 0)[$"Áthelyezés ({_chosen.Count})"],
                _chosen.Count > 0 ? Ui.Text.Key("count")[$"{_chosen.Count} sor kijelölve"] : null,
                Ui.Button.Key("none").Ghost.Sm.Disabled(_chosen.Count == 0).OnClick(_chosen.Clear)["Kijelölés törlése"]
            ],
            Ui.Table[
                Ui.TableColumns[Ui.TableColumn[Headings[0]], Ui.TableColumn[Headings[1]], Ui.TableColumn[Headings[2]]],
                Ui.TableRows[new[] { 11, 12, 13 }.Select(id => Ui.TableRow.Key(id)[
                    Ui.TableCell[Ui.Checkbox.Checked(_chosen.Contains(id)).OnChange(on => Choose(id, on))],
                    Ui.TableCell["2026-10-0" + (id - 10)],
                    Ui.TableCell[(id * 10) + " l"],
                    Ui.TableCell["Sofőr " + id]
                ])]
            ]
        ];

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
}
