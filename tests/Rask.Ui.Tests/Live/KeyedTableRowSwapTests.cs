using System.Globalization;
using Rask.Core;
using Rask.Core.Tests.Live;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.UiTests.Live;

// Eszaf, nightly .425, as the app writes it: kit rows keyed by their record, a cell of a checkbox and a cell of
// Up / Down / Remove buttons keyed by what they do. Moving a row past its neighbour left the old order on the
// page, or the old row's text under the new handlers, so the next click acted on the wrong row.
public partial class KeyedTableRowSwapTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task Moving_a_kit_row_up_shows_the_new_order_and_each_button_acts_on_its_own_row()
    {
        var table = new Columns();
        var browser = LiveBrowser.Open(table);

        await browser.Click(Button(browser, 1, "up"));

        Assert.Equal([7, 3, 9], table.Ids);
        Assert.Equal(["7", "3", "9"], Ids(browser));
    }

    [Fact]
    public async Task Every_move_after_the_first_acts_on_the_kit_row_it_was_clicked_in()
    {
        var table = new Columns();
        var browser = LiveBrowser.Open(table);

        await browser.Click(Button(browser, 1, "up"));
        await browser.Click(Button(browser, 2, "up"));
        await browser.Click(Button(browser, 0, "down"));
        await browser.Click(Button(browser, 2, "remove"));
        await browser.Click(Button(browser, 1, "up"));

        Assert.Equal([7, 9], table.Ids);
        Assert.Equal(["7", "9"], Ids(browser));
    }

    [Fact]
    public async Task A_ticked_kit_row_keeps_its_tick_when_it_moves()
    {
        var table = new Columns();
        var browser = LiveBrowser.Open(table);

        await browser.Click(Button(browser, 0, "pick"));
        await browser.Click(Button(browser, 1, "up"));

        Assert.Equal(["7", "3", "9"], Ids(browser));
        Assert.Equal(["no", "yes", "no"], [.. browser.Rows().Select(static row => Cell(row, 2).Text!)]);
    }

    private static string[] Ids(LiveBrowser browser) => [.. browser.Rows().Select(static row => Cell(row, 0).Text!)];

    // A cell's own text, under however many kit wrappers the cell puts around it.
    private static FrameDom.Node Cell(FrameDom.Node row, int index) =>
        LiveBrowser.Find(row.Children[index], static node => node.Tag is null)!;

    private static FrameDom.Node Button(LiveBrowser browser, int row, string key) =>
        LiveBrowser.Find(browser.Rows()[row], node => node.Key == key)!;

    private sealed class Columns : Component
    {
        private readonly HashSet<int> _picked = [];

        internal List<int> Ids { get; } = [3, 7, 9];

        protected override Component? Render() =>
            Ui.Table[
                Ui.TableColumns[Ui.TableColumn["Id"], Ui.TableColumn["Pick"], Ui.TableColumn["Picked"], Ui.TableColumn["Order"]],
                Ui.TableRows[Ids.Select((id, i) => Ui.TableRow.Key("col-" + id)[
                    Ui.TableCell[id.ToString(CultureInfo.InvariantCulture)],
                    Ui.TableCell[Ui.Button.Key("pick").OnClick(() => Toggle(id))["Pick"]],
                    Ui.TableCell[_picked.Contains(id) ? "yes" : "no"],
                    Ui.TableCell[
                        Ui.Button.Key("up").Ghost.Sm.OnClick(() => Swap(i, i - 1))["Up"],
                        Ui.Button.Key("down").Ghost.Sm.OnClick(() => Swap(i, i + 1))["Down"],
                        Ui.Button.Key("remove").Ghost.Sm.OnClick(() => Ids.RemoveAt(i))["Remove"]]])]];

        private void Toggle(int id)
        {
            if (!_picked.Remove(id))
            {
                _picked.Add(id);
            }
        }

        private void Swap(int a, int b)
        {
            if (b >= 0 && b < Ids.Count)
            {
                (Ids[a], Ids[b]) = (Ids[b], Ids[a]);
            }
        }
    }
}
