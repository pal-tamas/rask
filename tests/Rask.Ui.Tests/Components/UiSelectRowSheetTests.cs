using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     A select's and a pillbox's row are drawn by the kit sheet, keyed on their markers and on each part's place (#1316):
///     a row writes the group its parts read its state by and what differs from every other row, and the sheet the look
///     Flux's classes compute. Read from the markup and the COMPILED sheet.
/// </summary>
public partial class UiSelectRowSheetTests : global::Rask.Core.RaskMarkup
{
    private int? _picked = 1;

    private List<string> _held = ["b"];

    private Component Select() =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox[
            Ui.SelectOption.Value(0)["Photography"], Ui.SelectOption.Value(1)["Design services"]
        ];

    private Component Pillbox() =>
        Ui.Pillbox.Values(_held)[Ui.PillboxOption.Value("a")["Alpha"], Ui.PillboxOption.Value("b")["Beta"]];

    private static List<(string Selector, string Body)> Rules() =>
        UiStylesheetTests.Rules(UiStylesheet.Css).Select(r => (r.Selector, Body: r.Body.Replace(" ", "", StringComparison.Ordinal))).ToList();

    private static List<string?> Classes(IEnumerable<Rask.Testing.HtmlNode> rows) =>
        rows.SelectMany(row => row.DescendantsAndSelf()).Select(node => node.Attribute("class")).Where(c => c is not null).Distinct().ToList();

    [Fact]
    public void A_select_row_writes_its_group_and_nothing_its_neighbours_repeat()
    {
        var page = Page.Render(Select);

        var rows = page.FindAll("[data-ui-option]");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["group/option"], Classes(rows));
    }

    [Fact]
    public void A_pillbox_row_writes_its_group_alone()
    {
        var page = Page.Render(Pillbox);

        var rows = page.FindAll("[data-ui-listbox-option]");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["group/option"], Classes(rows));
    }

    [Fact]
    public void The_tick_is_hidden_in_every_row_and_shown_in_a_picked_one()
    {
        var rules = Rules();

        var hidden = Assert.Single(rules, r => r.Selector.EndsWith(":where([data-ui-listbox-option])>:where(div:first-child)>:where(svg)", StringComparison.Ordinal)
                                               && r.Body.Contains("display:none", StringComparison.Ordinal));
        var shown = Assert.Single(rules, r => r.Selector.StartsWith(":where([data-ui-option][data-selected])", StringComparison.Ordinal));

        Assert.Contains("height:calc(var(--spacing)*5)", hidden.Body, StringComparison.Ordinal);
        Assert.Contains(":where([data-ui-listbox-option][data-selected])>:where(div:first-child)>:where(svg)", shown.Selector, StringComparison.Ordinal);
        Assert.Equal("display:block", shown.Body);
    }

    [Fact]
    public void A_rows_outline_hidden_comes_after_the_keyboard_ring_at_the_same_weight()
    {
        var css = UiStylesheet.Css;

        var ring = css.IndexOf(":where(:focus-visible){", StringComparison.Ordinal);
        var row = css.IndexOf("outline-style:none", css.IndexOf(":where([data-ui-option],[data-ui-listbox-option]){--tw-outline-style", StringComparison.Ordinal), StringComparison.Ordinal);

        Assert.True(ring > 0 && row > 0, "both rules are in the sheet");
        Assert.True(row > ring, "the row's outline-hidden is written after the ring, so it wins at no weight");
    }

    [Fact]
    public void A_row_a_search_leaves_out_is_hidden_above_every_utility()
    {
        var css = UiStylesheet.Css;

        var rule = css.IndexOf(":where([data-ui-option],[data-ui-listbox-option])[data-hidden]{display:none", StringComparison.Ordinal);
        var corrections = css.LastIndexOf("@layer rask-ui-corrections", rule, StringComparison.Ordinal);
        var closedBefore = css.LastIndexOf("@layer ", rule, StringComparison.Ordinal);

        Assert.True(rule > 0, "the rule is in the sheet");
        Assert.True(corrections >= 0 && corrections == closedBefore, "it sits in rask-ui-corrections, above utilities");
    }
}
