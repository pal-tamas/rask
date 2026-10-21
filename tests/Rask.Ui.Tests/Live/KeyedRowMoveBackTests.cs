using Rask.Core.Tests.Live;

namespace Rask.UiTests.Live;

// Eszaf #16, nightly .427: the report form's columns, kit rows keyed "column-<id>". After the columns are added and two
// are grouped to the front, a row moved down and then up again lost the second move: the page kept the row where the
// first had put it. Every render is diffed and REPLAYED onto the page the browser holds (LiveBrowser), and every click
// is read from that page.
public class KeyedRowMoveBackTests
{
    [Fact]
    public async Task A_kit_row_moved_down_and_then_up_again_lands_where_the_last_move_put_it()
    {
        var form = new ReportColumnsDraft();
        var browser = LiveBrowser.Open(form);
        await browser.Click(Key(browser.Page.At(), "add"));
        await browser.Change(Tick(browser, "Mennyiség", 2), "true");
        await browser.Change(Tick(browser, "EO", 2), "true");
        await browser.Change(Tick(browser, "Jármű", 1), "true");
        await browser.Change(Tick(browser, "Termék", 1), "true");
        Assert.Equal(["Jármű", "Termék", "Km", "Dátum", "Mennyiség", "EO"], Names(browser));

        await browser.Click(Press(browser, "Dátum", "up"));
        Assert.Equal(["Jármű", "Termék", "Dátum", "Km", "Mennyiség", "EO"], Names(browser));
        await browser.Click(Press(browser, "Mennyiség", "down"));
        Assert.Equal(["Jármű", "Termék", "Dátum", "Km", "EO", "Mennyiség"], Names(browser));
        await browser.Click(Press(browser, "Mennyiség", "up"));
        Assert.Equal(["Jármű", "Termék", "Dátum", "Km", "Mennyiség", "EO"], Names(browser));
        await browser.Click(Press(browser, "Km", "remove"));

        Assert.Equal(["Jármű", "Termék", "Dátum", "Mennyiség", "EO"], Names(browser));
        Assert.Equal(form.Names, Names(browser));
    }

    private static string[] Names(LiveBrowser browser) => [.. browser.Rows().Select(static row => Text(row.Children[0]))];

    private static string Text(FrameDom.Node node) => node.Tag is null ? node.Text! : string.Concat(node.Children.Select(Text));

    private static FrameDom.Node Row(LiveBrowser browser, string name) => browser.Rows().Single(row => Text(row.Children[0]) == name);

    private static FrameDom.Node Press(LiveBrowser browser, string name, string key)
    {
        // A button with a tooltip sits inside the tooltip's wrapper, which is what carries the key.
        var button = LiveBrowser.Find(Key(Row(browser, name), key), static node => node.Tag == "button")!;
        Assert.False(button.Attributes.ContainsKey("disabled"), $"the {key} button of {name} is disabled");
        return button;
    }

    private static FrameDom.Node Tick(LiveBrowser browser, string name, int cell) =>
        LiveBrowser.Find(Row(browser, name).Children[cell], static node => node.Tag == "input")!;

    private static FrameDom.Node Key(FrameDom.Node within, string key) => LiveBrowser.Find(within, node => node.Key == key)!;
}
