using Rask.Core.Live;


#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.Core.Tests.Live;

// Eszaf, nightly .425: keyed rows whose buttons swap a row with the one before it. Clicking the second row's
// button left the page showing the old order, or the old row's text under the new handlers, so the next click
// acted on the wrong row. Every render here is diffed and the ops are REPLAYED onto the page the browser holds,
// and every click is read from that page — the way a browser reads it.
//
// Rows written straight under the <table> were the ones that broke: the browser's parser puts them in a <tbody>
// the frames did not have, so every path through the table was one level off and the move found no row to move.
public partial class KeyedRowSwapTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Moving_a_keyed_row_up_shows_the_new_order_and_each_button_acts_on_its_own_row(bool bare)
    {
        var table = new SwapTable(bare);
        var browser = LiveBrowser.Open(table);

        await browser.Click(RowButton(browser, 1, "up"));

        Assert.Equal(["7", "3", "9"], Ids(browser));
        Assert.Equal([7, 3, 9], table.Ids);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_second_move_after_the_first_acts_on_the_row_it_was_clicked_in(bool bare)
    {
        var table = new SwapTable(bare);
        var browser = LiveBrowser.Open(table);

        await browser.Click(RowButton(browser, 1, "up"));
        await browser.Click(RowButton(browser, 2, "up"));
        await browser.Click(RowButton(browser, 0, "down"));

        Assert.Equal([9, 7, 3], table.Ids);
        Assert.Equal(["9", "7", "3"], Ids(browser));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removing_a_keyed_row_after_a_move_leaves_the_others_with_their_own_buttons(bool bare)
    {
        var table = new SwapTable(bare);
        var browser = LiveBrowser.Open(table);

        await browser.Click(RowButton(browser, 2, "up"));
        await browser.Click(RowButton(browser, 0, "remove"));

        Assert.Equal([9, 7], table.Ids);
        Assert.Equal(["9", "7"], Ids(browser));
        await browser.Click(RowButton(browser, 1, "up"));
        Assert.Equal(["7", "9"], Ids(browser));
    }

    // Eszaf #16: a swap of two neighbours and the swap back are the same ops, byte for byte. Each is a change all the
    // same, which is why a session may never drop a diff for repeating the one before it (RepeatedDiffTests).
    [Fact]
    public void Swapping_two_keyed_rows_back_writes_the_very_ops_that_swapped_them()
    {
        var there = Reply.To(Ul[Li.Key("a")["a"], Li.Key("b")["b"]], Ul[Li.Key("b")["b"], Li.Key("a")["a"]]);
        var back = Reply.To(Ul[Li.Key("b")["b"], Li.Key("a")["a"]], Ul[Li.Key("a")["a"], Li.Key("b")["b"]]);

        there.Apply();
        back.Apply();

        var move = Assert.Single(there.Ops);
        Assert.Equal(EditOpKind.PermutationBatch, move.Kind);
        Assert.Equal(move.Moves, Assert.Single(back.Ops).Moves);
        Assert.Equal(there.Expected, there.Before.Html);
        Assert.Equal(back.Expected, back.Before.Html);
    }

    private static string[] Ids(LiveBrowser browser) =>
        [.. browser.Rows().Select(static row => row.Children[0].Children[0].Text!)];

    private static FrameDom.Node RowButton(LiveBrowser browser, int row, string key) =>
        browser.Rows()[row].Children[1].Children.Single(button => button.Key == key);

    // Rows hold a text cell and a cell of buttons keyed by what they do, as the reported table does.
    // Under a <tbody> the page writes, or straight under the <table> when `bare`.
    private sealed class SwapTable(bool bare) : Component
    {
        internal List<int> Ids { get; } = [3, 7, 9];

        protected override Component? Render()
        {
            var rows = Ids.Select((id, i) => Tr.Key("r" + id)[
                Td[id.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                Td[
                    Button.Key("up").OnClick(() => Swap(i, i - 1))["Up"],
                    Button.Key("down").OnClick(() => Swap(i, i + 1))["Down"],
                    Button.Key("remove").OnClick(() => Ids.RemoveAt(i))["Remove"]]]);
            return bare ? Table[rows] : Table[Tbody[rows]];
        }

        private void Swap(int a, int b)
        {
            if (b < 0 || b >= Ids.Count)
            {
                return;
            }

            (Ids[a], Ids[b]) = (Ids[b], Ids[a]);
        }
    }
}
