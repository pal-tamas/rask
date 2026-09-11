namespace Rask.Ui.Tests.Components;

/// <summary>
///     <see cref="UiTable" /> and <see cref="UiList" /> ARE their elements.
/// </summary>
/// <remarks>
///     <para>
///     Both derive from <c>Element</c>, and what that buys is the reason for the tests below: every step an
///     element takes works on them with nothing redeclared, in the attribute order Core documents and its
///     own element tests pin — id, class, style, data-*, role, tabindex, aria-*.
///     </para>
///     <para>
///     The class assertions check COMPOSITION rather than a literal. The kit's classes are the kit's
///     business and will change; what must not change is that a call site's <c>.Class(…)</c> is added to
///     them instead of replacing them, which is the failure an override of <c>Class</c> would have had.
///     </para>
/// </remarks>
public partial class UiTableAndListTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_table_is_one_table_element_with_its_children_inside()
    {
        var html = UiTable[Tbody[Tr[Td["x"]]]].ToHtml();

        Assert.StartsWith("<table ", html, StringComparison.Ordinal);
        Assert.EndsWith("</table>", html, StringComparison.Ordinal);
        Assert.Contains("<tbody><tr><td>x</td></tr></tbody>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<div", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_pads_its_cells_through_the_kit_marker_not_a_descendant_variant()
    {
        // The previous version's documentation said it padded and its markup did not; every console table
        // padded each cell by hand. The padding is a stylesheet rule on `ui-table` — a `[&_td]:px-3`
        // variant would out-specify a cell's own `px-0` and win without saying so.
        var html = UiTable.ToHtml();

        Assert.Matches("class=\"ui-table [^\"]*\"", html);
        Assert.DoesNotContain("[&amp;_td]", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_pads_its_rows_through_the_kit_marker_not_a_descendant_variant()
    {
        var html = UiList.ToHtml();

        Assert.Matches("class=\"ui-list [^\"]*\"", html);
        Assert.DoesNotContain("[&amp;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_site_class_is_added_to_the_kits_not_substituted_for_it()
    {
        var html = UiTable.Class("mb-0").ToHtml();

        Assert.Matches("class=\"ui-table [^\"]* mb-0\"", html);
    }

    [Fact]
    public void Every_element_step_reaches_the_table_in_the_documented_order()
    {
        // Nothing here is declared on UiTable. Id, Style, Data, Role and Aria all come from Element, which
        // is the whole point of deriving from it instead of mirroring props one at a time.
        var html = UiTable
            .Id("orders")
            .Class("mb-0")
            .Style("table-layout:fixed")
            .Data("testid", "orders")
            .Role("grid")
            .Aria(("label", "Orders"))
            .ToHtml();

        var id = html.IndexOf("id=", StringComparison.Ordinal);
        var cls = html.IndexOf("class=", StringComparison.Ordinal);
        var style = html.IndexOf("style=", StringComparison.Ordinal);
        var data = html.IndexOf("data-testid=", StringComparison.Ordinal);
        var role = html.IndexOf("role=", StringComparison.Ordinal);
        var aria = html.IndexOf("aria-label=", StringComparison.Ordinal);

        Assert.True(id >= 0 && id < cls && cls < style && style < data && data < role && role < aria, html);
    }

    [Fact]
    public void A_table_does_not_scroll_unless_asked()
    {
        Assert.DoesNotContain("overflow-x-auto", UiTable[Tbody].ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_scrolling_table_is_a_box_with_the_table_inside()
    {
        var html = UiTable.Scroll(true)[Tbody[Tr[Td["x"]]]].ToHtml();

        Assert.StartsWith("<div class=\"overflow-x-auto", html, StringComparison.Ordinal);
        Assert.EndsWith("</table></div>", html, StringComparison.Ordinal);
        Assert.Contains("<tbody><tr><td>x</td></tr></tbody>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scrolling_table_keeps_every_attribute_on_the_table_not_the_box()
    {
        // The box is presentation. A selector like `#orders tbody tr`, a screen reader's reading of the
        // table and a call site's classes all have to find the <table> exactly where they would without it.
        var html = UiTable
            .Scroll(true)
            .Id("orders")
            .Class("mb-0")
            .Data("testid", "orders")
            .Aria(("label", "Orders"))[Tbody]
            .ToHtml();

        var box = html[..html.IndexOf("<table", StringComparison.Ordinal)];
        var table = html[html.IndexOf("<table", StringComparison.Ordinal)..];

        Assert.Equal("<div class=\"overflow-x-auto rounded-xl border border-base-300 bg-base-100\">", box);
        Assert.StartsWith("<table id=\"orders\" class=\"ui-table ", table, StringComparison.Ordinal);
        Assert.Contains(" mb-0\" data-testid=\"orders\" aria-label=\"Orders\">", table, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_is_a_ul_by_default()
    {
        var html = UiList[Li["one"]].ToHtml();

        Assert.StartsWith("<ul ", html, StringComparison.Ordinal);
        Assert.Contains("<li>one</li>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("list-decimal", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ordered_list_is_an_ol_and_shows_its_numbers()
    {
        // The difference is meaning, not styling — and the reset every Tailwind app ships strips the
        // markers, so an ordered list that did not turn them back on would draw no ordinals at all.
        var html = UiList.Ordered(true)[Li["first"], Li["second"]].ToHtml();

        Assert.StartsWith("<ol ", html, StringComparison.Ordinal);
        Assert.EndsWith("</ol>", html, StringComparison.Ordinal);
        Assert.Contains("list-decimal", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Element_steps_reach_the_list_too()
    {
        var html = UiList.Id("log").Class("text-sm").Data("testid", "log").ToHtml();

        Assert.Contains("id=\"log\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"log\"", html, StringComparison.Ordinal);
        Assert.Matches("class=\"ui-list [^\"]* text-sm\"", html);
    }
}
