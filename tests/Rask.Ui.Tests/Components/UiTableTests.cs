using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     <see cref="UiTable" /> and its parts: the elements they render, where a call site's attributes land,
///     and what each prop writes.
/// </summary>
/// <remarks>
///     How it LOOKS is <c>Flux/Parity/TableParity</c>'s, measured against Flux's own page. These hold the
///     markup contract a stylesheet cannot see.
/// </remarks>
public partial class UiTableTests : global::Rask.Core.RaskMarkup
{
    private static global::Rask.Core.Component Orders() =>
        Ui.Table[
            Ui.TableColumns[Ui.TableColumn["Customer"], Ui.TableColumn["Amount"]],
            Ui.TableRows[Ui.TableRow[Ui.TableCell["Lindsey"], Ui.TableCell["$49.00"]]]
        ];

    [Fact]
    public void A_table_is_a_box_holding_a_scroll_area_holding_the_table()
    {
        var html = Orders().ToHtml();

        Assert.StartsWith("<div class=\"flex flex-col *:data-ui-pagination:shrink-0\"><div class=\"relative block overflow-auto\"><table ", html, StringComparison.Ordinal);
        Assert.EndsWith("</table></div></div>", html, StringComparison.Ordinal);
    }

    // An owner-approved departure from Flux (2026-10-14): Flux's frame is static, so a checkbox's hidden input or a
    // hidden heading in a cell escapes it and the whole page scrolls sideways on a phone.
    [Fact]
    public void The_scroll_area_holds_what_its_cells_place_absolutely()
    {
        var html = Ui.Table[
            Ui.TableColumns[Ui.TableColumn[Span.Class("sr-only")["Actions"]]],
            Ui.TableRows[Ui.TableRow[Ui.TableCell[Ui.Checkbox.Label("Pick")]]]
        ].ToHtml();

        Assert.Contains("<div class=\"relative block overflow-auto\"><table ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_part_is_its_table_element_and_carries_its_marker()
    {
        var html = Orders().ToHtml();

        Assert.Contains("<table class=\"", html, StringComparison.Ordinal);
        Assert.Matches("<table [^>]*data-ui-table", html);
        Assert.Matches("<thead data-ui-columns><tr><th [^>]*data-ui-column", html);
        Assert.Matches("<tbody data-ui-rows><tr data-ui-row><td [^>]*data-ui-cell", html);
    }

    [Fact]
    public void A_heading_wraps_its_label_and_a_cell_does_not()
    {
        var html = Orders().ToHtml();

        Assert.Contains("<div class=\"flex\">Customer</div></th>", html, StringComparison.Ordinal);
        Assert.Contains(">Lindsey</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Element_steps_land_on_the_table_and_the_box_takes_its_own_classes()
    {
        var html = Ui.Table.Id("orders").Class("mb-0").Data("testid", "orders").Aria(("label", "Orders")).ContainerClass("max-h-80").ToHtml();

        var box = html[..html.IndexOf("<div class=\"relative block overflow-auto\"", StringComparison.Ordinal)];
        var table = html[html.IndexOf("<table", StringComparison.Ordinal)..];

        Assert.Equal("<div class=\"flex flex-col *:data-ui-pagination:shrink-0 max-h-80\">", box);
        Assert.StartsWith("<table id=\"orders\" class=\"", table, StringComparison.Ordinal);
        Assert.Matches(" mb-0\" data-ui-table data-testid=\"orders\" aria-label=\"Orders\">", table);
    }

    [Fact]
    public void Element_steps_reach_every_part()
    {
        var html = Ui.Table[
            Ui.TableColumns.Id("head")[Ui.TableColumn.Id("col").Class("w-40")["Customer"]],
            Ui.TableRows.Id("body")[Ui.TableRow.Id("row")[Ui.TableCell.Id("cell").Class("py-0")["Lindsey"]]]
        ].ToHtml();

        Assert.Contains("<thead id=\"head\" ", html, StringComparison.Ordinal);
        Assert.Matches("<th id=\"col\" class=\"[^\"]* w-40\"", html);
        Assert.Contains("<tbody id=\"body\" ", html, StringComparison.Ordinal);
        Assert.Contains("<tr id=\"row\" ", html, StringComparison.Ordinal);
        Assert.Matches("<td id=\"cell\" class=\"[^\"]* py-0\"", html);
    }

    [Fact]
    public void A_cell_pads_itself_at_zero_specificity_so_its_own_padding_class_wins()
    {
        var html = Ui.TableCell.Class("py-0").ToHtml();

        Assert.Contains("[:where(&amp;)]:py-3", html, StringComparison.Ordinal);
        Assert.DoesNotMatch("[\" ]py-3[\" ]", html);
    }

    [Fact]
    public void A_bleeding_table_marks_its_box_and_pulls_it_out_by_the_gutter()
    {
        var plain = Ui.Table.ToHtml();

        var bleeding = Ui.Table.Bleed().ToHtml();

        Assert.DoesNotContain("data-ui-table-bleed", plain, StringComparison.Ordinal);
        Assert.Matches("^<div class=\"flex flex-col \\*:data-ui-pagination:shrink-0 -mx-\\[var\\(--ui-bleed,1.5rem\\)\\][^\"]*\" data-ui-table-bleed=\"\">", bleeding);
    }

    [Fact]
    public void A_bleeding_table_moves_its_edge_columns_itself_so_no_cell_carries_a_bleed_rule()
    {
        var row = Ui.TableRow[Ui.TableCell["A"], Ui.TableCell["B"]];

        var plain = Ui.Table[Ui.TableRows[row]].ToHtml();
        var bleeding = Ui.Table.Bleed()[Ui.TableRows[row]].ToHtml();

        Assert.DoesNotContain("--ui-bleed", plain, StringComparison.Ordinal);
        Assert.Matches("<table class=\"[^\"]*\\[:where\\(&amp;_\\[data-ui-cell\\]:first-child\\)\\]:ps-\\[var\\(--ui-bleed,1.5rem\\)\\]", bleeding);
        Assert.Matches("<table class=\"[^\"]*\\[:where\\(&amp;_\\[data-ui-column\\]:last-child\\)\\]:pe-\\[var\\(--ui-bleed,1.5rem\\)\\]", bleeding);
        Assert.DoesNotMatch("<td class=\"[^\"]*bleed", bleeding);
    }

    [Fact]
    public void A_plain_cell_writes_Fluxs_class_list_with_its_defaults_at_zero_specificity()
    {
        var html = Ui.TableCell["$49.00"].ToHtml();

        var classes = System.Text.RegularExpressions.Regex.Match(html, "class=\"([^\"]*)\"").Groups[1].Value.Replace("&amp;", "&", StringComparison.Ordinal);

        Assert.Equal(
            "[:where(&)]:py-3 [:where(&:not(:first-child))]:ps-3 [:where(&:not(:last-child))]:pe-3 [:where(&)]:text-sm "
            + "[:where(&)]:text-zinc-500 dark:[:where(&)]:text-zinc-300 not-in-[tr:first-child]:border-t border-zinc-800/10 dark:border-white/20",
            classes);
    }

    [Fact]
    public void The_pager_sits_in_the_box_after_the_scroll_area()
    {
        var html = Ui.Table.Paginate(Nav.Id("pager")).ToHtml();

        Assert.EndsWith("</div><nav id=\"pager\"></nav></div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sticky_heading_row_holds_to_the_top_above_sticky_cells()
    {
        var html = Ui.TableColumns.Sticky().Class("bg-white").ToHtml();

        Assert.Contains("<thead class=\"sticky top-0 z-20 bg-white\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sticky_column_and_cell_hold_to_the_left_and_carry_the_shadow_hook()
    {
        var column = Ui.TableColumn.Sticky()["ID"].ToHtml();

        var cell = Ui.TableCell.Sticky()["428"].ToHtml();

        Assert.Contains(" sticky left-0 z-10 after:", column, StringComparison.Ordinal);
        Assert.Contains(" sticky left-0 z-10 after:", cell, StringComparison.Ordinal);
        Assert.DoesNotContain("sticky", Ui.TableCell["428"].ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_strong_cell_takes_the_heading_colour_and_weight()
    {
        var plain = Ui.TableCell["$49.00"].ToHtml();

        var strong = Ui.TableCell.Variant(Ui.TableCellVariant.Strong)["$49.00"].ToHtml();

        Assert.Contains("text-zinc-500", plain, StringComparison.Ordinal);
        Assert.Contains("font-medium [:where(&amp;)]:text-zinc-800 dark:[:where(&amp;)]:text-white", strong, StringComparison.Ordinal);
        Assert.DoesNotContain("text-zinc-500", strong, StringComparison.Ordinal);
    }

    [Fact]
    public void Alignment_moves_a_cells_text_and_a_headings_label()
    {
        var cell = Ui.TableCell.End["$49.00"].ToHtml();

        var column = Ui.TableColumn.Center["Amount"].ToHtml();

        Assert.Contains(" [:where(&amp;)]:text-end", cell, StringComparison.Ordinal);
        Assert.Contains("<div class=\"flex justify-center\">Amount</div>", column, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sortable_heading_is_a_button_with_a_hidden_chevron_until_it_is_sorted()
    {
        var html = Ui.TableColumn.Sortable()["Date"].ToHtml();

        Assert.Matches("<th class=\"[^\"]*group/sortable[^\"]*\"", html);
        Assert.Matches("<button [^>]*data-ui-table-sortable=\"\" type=\"button\"><div>Date</div>", html);
        Assert.Contains("<div class=\"opacity-0 group-hover/sortable:opacity-100\"><svg ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sortable_heading_waits_on_its_own_sort_and_a_plain_one_asks_for_nothing()
    {
        var sortable = Ui.TableColumn.Sortable().OnSort(() => { })["Date"].ToHtml();
        var plain = Ui.TableColumn["Customer"].ToHtml();

        // Bare: the cell carries the click, so it is the control the runtime marks.
        Assert.Matches("<th [^>]*data-ui-column data-rask-loading[ >]", sortable);
        Assert.DoesNotContain("data-rask-loading", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sheet_dims_the_rows_by_the_marks_the_table_and_its_pager_carry()
    {
        var sheet = File.ReadAllText(Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "Styles", "ui.css"));
        var pager = Ui.Pagination.Paginator(new UiPaginator { Page = 1, PerPage = 5, Total = 24 }).OnPage(_ => { });

        var html = Ui.Table.Paginate(pager)[
            Ui.TableColumns[Ui.TableColumn.Sortable().OnSort(() => { })["Date"]],
            Ui.TableRows[Ui.TableRow[Ui.TableCell["Jul 29"]]]
        ].ToHtml();

        // A sortable heading inside the table, the pager right after what holds it, and the table's own mark — on a
        // table that is not drawn from a query, which shows skeleton rows instead.
        Assert.Contains(
            ":is([data-ui-table]:is([data-loading], :has([data-ui-column][data-loading])) [data-ui-rows],", sheet, StringComparison.Ordinal);
        Assert.Contains(
            ":is(:has(> [data-ui-table]), :has(> * > [data-ui-table])):has(+ [data-ui-pagination][data-loading]) [data-ui-rows]):not([data-rask-skeleton] *) {",
            sheet,
            StringComparison.Ordinal);
        Assert.Matches("<table [^>]*data-ui-table[^>]*>.*<tbody data-ui-rows>.*</table></div><div [^>]*data-ui-pagination ", html);
    }

    [Fact]
    public void A_sortable_heading_at_the_end_draws_its_chevron_before_the_label()
    {
        var atStart = Ui.TableColumn.Sortable()["Amount"].ToHtml();

        var atEnd = Ui.TableColumn.End.Sortable()["Amount"].ToHtml();

        Assert.Contains("<button class=\"-my-1 -ms-2 flex items-center ", atStart, StringComparison.Ordinal);
        Assert.DoesNotContain("flex-row-reverse", atStart, StringComparison.Ordinal);
        Assert.Contains("<div class=\"flex justify-end\"><button class=\"-my-1 -ms-8 flex flex-row-reverse items-center ", atEnd, StringComparison.Ordinal);
        Assert.Contains("type=\"button\"><div>Amount</div><div class=", atEnd, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sorted_heading_shows_which_way_it_runs()
    {
        var ascending = Ui.TableColumn.Sortable().Sorted()["Date"].ToHtml();

        var descending = Ui.TableColumn.Sortable().Sorted().Direction(Ui.TableColumnDirection.Desc)["Date"].ToHtml();

        Assert.Contains("d=\"M11.78 9.78", ascending, StringComparison.Ordinal);
        Assert.Contains("d=\"M4.22 6.22", descending, StringComparison.Ordinal);
        Assert.DoesNotContain("opacity-0", ascending + descending, StringComparison.Ordinal);
    }

    [Fact]
    public void The_chevron_is_decoration_a_screen_reader_skips()
    {
        var html = Ui.TableColumn.Sortable().Sorted()["Date"].ToHtml();

        Assert.Matches("<svg [^>]*aria-hidden=\"true\"[^>]*viewBox=\"0 0 16 16\"", html);
    }

    [Fact]
    public void A_heading_that_is_not_sortable_draws_no_button_even_when_told_it_is_sorted()
    {
        var html = Ui.TableColumn.Sorted().Direction(Ui.TableColumnDirection.Desc).OnSort(() => { })["Date"].ToHtml();

        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clicking_a_sortable_heading_fires_its_sort()
    {
        var sorts = 0;
        var page = Page.Render(Ui.Table[Ui.TableColumns[Ui.TableColumn.Sortable().OnSort(() => sorts++)["Date"]]]);

        await page.On("th").Click();

        Assert.Equal(1, sorts);
    }

    [Fact]
    public async Task A_heading_with_a_click_of_its_own_runs_it_and_then_sorts()
    {
        var log = new List<string>();
        var page = Page.Render(Ui.Table[Ui.TableColumns[
            Ui.TableColumn.Sortable().OnClick(() => log.Add("click")).OnSort(() => log.Add("sort"))["Date"]
        ]]);

        await page.On("th").Click();

        Assert.Equal(["click", "sort"], log);
    }

    [Fact]
    public void A_heading_that_is_not_sortable_wires_no_click_for_its_sort()
    {
        var page = Page.Render(Ui.Table[Ui.TableColumns[Ui.TableColumn.OnSort(() => { })["Date"]]]);

        Assert.DoesNotContain("data-rask-on-click", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_keeps_its_key()
    {
        var html = Ui.TableRow.Key("order-428")[Ui.TableCell["428"]].ToHtml();

        Assert.Contains("data-rask-key=\"order-428\"", html, StringComparison.Ordinal);
    }
}
