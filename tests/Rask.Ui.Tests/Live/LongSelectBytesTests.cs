using System.Globalization;
using System.Text;
using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Live;

/// <summary>
///     What a select of a thousand options costs on the page and on the wire: drawn whole, as every list was before it
///     was windowed (and as one with a group still is), and windowed. Each test writes its numbers to the test output.
/// </summary>
public partial class LongSelectBytesTests(ITestOutputHelper output) : global::Rask.Core.RaskMarkup
{
    private const int Count = 1000;

    private int? _picked;

    private static IEnumerable<Component> Options() =>
        Enumerable.Range(0, Count).Select(id => Ui.SelectOption.Key(id).Value(id)[$"Vehicle {id:0000}"]);

    private Component Windowed() =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox.Searchable()[Options()];

    // The same options under one heading: a list with a group is drawn whole, row for row as every list was before.
    private Component Whole() =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox.Searchable()[Ui.SelectGroup.Label("Fleet")[Options()]];

    private static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);

    private static int Rows(Page page) => page.FindAll("[role=\"option\"]").Count;

    private static int Elements(Page page) => page.FindAll("[data-ui-select] *").Count;

    private void Say(string what, int value) =>
        output.WriteLine(what + ": " + value.ToString("N0", CultureInfo.InvariantCulture));

    [Fact]
    public void A_windowed_select_of_a_thousand_options_draws_a_twentieth_of_the_markup()
    {
        var whole = Page.Render(Whole);
        var windowed = Page.Render(Windowed);

        var (wholeBytes, windowedBytes) = (Bytes(whole.Html), Bytes(windowed.Html));

        Say("rows drawn whole", Rows(whole));
        Say("rows drawn windowed", Rows(windowed));
        Say("elements drawn whole", Elements(whole));
        Say("elements drawn windowed", Elements(windowed));
        Say("first render bytes whole", wholeBytes);
        Say("first render bytes windowed", windowedBytes);
        Assert.Equal(Count, Rows(whole));
        Assert.Equal(20, Rows(windowed));
        Assert.True(windowedBytes * 20 < wholeBytes, $"{windowedBytes} bytes windowed against {wholeBytes} whole.");
    }

    [Fact]
    public async Task A_scroll_of_one_box_sends_the_rows_it_brings_and_no_more()
    {
        var page = Page.Render(Windowed);

        await page.On("[data-rask-list-window]").Raise("scroll", "{\"target\":{\"scrollTop\":320,\"clientHeight\":320}}");
        await page.On("[data-rask-list-window]").Raise("scroll", "{\"target\":{\"scrollTop\":16000,\"clientHeight\":320}}");

        var frames = DiffGuardAttribute.Frames();
        Say("scroll by one box (10 rows) bytes", Bytes(frames[0]));
        Say("scroll to the middle (30 new rows) bytes", Bytes(frames[1]));
        Assert.Equal(2, frames.Count);
        Assert.InRange(Bytes(frames[0]), 1, 16_000);
        Assert.InRange(Bytes(frames[1]), 1, 45_000);
    }

    [Fact]
    public async Task A_key_typed_into_the_search_of_a_windowed_select_sends_the_rows_it_leaves()
    {
        var page = Page.Render(Windowed);

        await page.On("[data-ui-select-search] input").Input("0999");

        var frame = Assert.Single(DiffGuardAttribute.Frames());
        Say("search narrowing to one row bytes", Bytes(frame));
        Assert.InRange(Bytes(frame), 1, 25_000);
    }

    [Fact]
    public async Task A_pick_in_a_windowed_select_sends_as_little_as_one_in_a_whole_one()
    {
        var whole = Page.Render(Whole);
        await whole.On("#" + whole.FindAll("[role=\"option\"]")[2].Attribute("id")).Click();
        var wholeFrame = Bytes(DiffGuardAttribute.Frames()[^1]);
        _picked = null;

        var windowed = Page.Render(Windowed);
        await windowed.On("[aria-posinset=\"3\"]").Click();
        var windowedFrame = Bytes(DiffGuardAttribute.Frames()[^1]);

        Say("pick bytes whole", wholeFrame);
        Say("pick bytes windowed", windowedFrame);
        Assert.Equal(2, _picked);
        Assert.True(windowedFrame <= wholeFrame + 200, $"{windowedFrame} bytes windowed against {wholeFrame} whole.");
    }
}
