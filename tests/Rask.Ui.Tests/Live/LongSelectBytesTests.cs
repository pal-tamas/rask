using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Live;

/// <summary>
///     What a select of a thousand options costs on the page and on the wire: drawn whole, as every list was before it
///     was windowed (and as one with a group still is), and windowed. Each test writes its numbers to the test output.
/// </summary>
/// <remarks>
///     The budgets are the exact counts, so a byte more is a red: a row's look is the kit sheet's, keyed on its marker
///     and its place (#1316), and a row writes only what differs. Lower one when a change takes bytes off; raise one only
///     on purpose. Before #1316 a row was 1,332 bytes, a thousand drawn whole 1,229,821, a hundred 127,126, a scroll of one
///     box 13,374, a jump to the middle 40,192, a search narrowing to one row 1,349.
/// </remarks>
public partial class LongSelectBytesTests(ITestOutputHelper output) : global::Rask.Core.RaskMarkup
{
    private const int Count = 1000;

    // The budgets, in bytes (#1316): exact, so a byte more is a red.
    private const int RowBudget = 612;
    private const int WholeBudget = 526_821;
    // +40 on purpose (2026-11-20): each spacer says it shows skeleton rows (`data-ui-list-unseen`, ui.css).
    private const int WindowedBudget = 17_973;
    private const int HundredBudget = 56_826;
    private const int ScrollBudget = 6_174;
    private const int MiddleBudget = 18_592;
    private const int SearchBudget = 629;

    private int? _picked;

    private static IEnumerable<Component> Options(int count = Count) =>
        Enumerable.Range(0, count).Select(id => Ui.SelectOption.Key(id).Value(id)[$"Vehicle {id:0000}"]);

    private Component Windowed() =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox.Searchable()[Options()];

    // The same options under one heading: a list with a group is drawn whole, row for row as every list was before.
    private Component Whole() =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox.Searchable()[Ui.SelectGroup.Label("Fleet")[Options()]];

    // The longest list still drawn whole (UiListWindow.Above): every row of it on the first render.
    private Component Hundred() =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox.Searchable()[Options(UiListWindow.Above)];

    // Counted as the first select of a process writes it: a select's ids and its field's are numbered by how many the
    // process drew before (`uisel-37-opt-0`, `f-field-12`), so the same markup is a digit or two longer late in a run —
    // and an exact budget would be red for which tests happened to go first.
    private static int Bytes(string text) => Encoding.UTF8.GetByteCount(Numbered().Replace(text, "${name}-1"));

    [GeneratedRegex(@"(?<name>uisel|f-field)-\d+")]
    private static partial Regex Numbered();

    // The first row's markup: from its tag to the next row's.
    private static string FirstRow(string html)
    {
        var role = html.IndexOf("role=\"option\"", StringComparison.Ordinal);
        var first = html.LastIndexOf("<div", role, StringComparison.Ordinal);
        var second = html.LastIndexOf("<div", html.IndexOf("role=\"option\"", role + 1, StringComparison.Ordinal), StringComparison.Ordinal);
        return html[first..second];
    }

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
        Assert.InRange(wholeBytes, 1, WholeBudget);
        Assert.InRange(windowedBytes, 1, WindowedBudget);
    }

    [Fact]
    public void A_row_of_a_windowed_select_writes_its_marks_and_words_and_the_tick()
    {
        var page = Page.Render(Windowed);

        var row = FirstRow(page.Html);

        Say("row bytes", Bytes(row));
        Assert.Contains("aria-posinset=\"1\"", row, StringComparison.Ordinal);
        Assert.InRange(Bytes(row), 1, RowBudget);
    }

    [Fact]
    public void A_hundred_options_drawn_whole_cost_their_rows_and_no_more()
    {
        var page = Page.Render(Hundred);

        var bytes = Bytes(page.Html);

        Say("rows drawn", Rows(page));
        Say("first render bytes", bytes);
        Assert.Equal(UiListWindow.Above, Rows(page));
        Assert.InRange(bytes, 1, HundredBudget);
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
        Assert.InRange(Bytes(frames[0]), 1, ScrollBudget);
        Assert.InRange(Bytes(frames[1]), 1, MiddleBudget);
    }

    [Fact]
    public async Task A_key_typed_into_the_search_of_a_windowed_select_sends_the_rows_it_leaves()
    {
        var page = Page.Render(Windowed);

        await page.On("[data-ui-select-search] input").Raise("pause", "{\"value\":\"0999\"}");

        var frame = Assert.Single(DiffGuardAttribute.Frames());
        Say("search narrowing to one row bytes", Bytes(frame));
        Assert.InRange(Bytes(frame), 1, SearchBudget);
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
