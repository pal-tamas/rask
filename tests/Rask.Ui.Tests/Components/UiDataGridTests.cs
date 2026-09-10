using System.Globalization;

namespace Rask.Ui.Tests.Components;

/// <summary>
///     The data grid: the chain that describes its columns, and what it draws from them.
/// </summary>
/// <remarks>
///     The first test in this file is the one that matters most, and it is a COMPILATION test as much as
///     a rendering one: the whole reason the grid has a chain shape of its own is that a column written
///     as a flat child cannot infer its own lambda's parameter. If the factory form ever stops
///     compiling, every other test here goes with it.
/// </remarks>
public partial class UiDataGridTests : global::Rask.Core.RaskMarkup
{
    private sealed record Product(int Id, string Name, string Category, decimal Price);

    private static readonly Product[] Catalog =
    [
        new(1, "Anvil", "Hardware", 30m),
        new(2, "Rope", "Hardware", 8m),
        new(3, "Bread", "Grocery", 2m),
        new(4, "Cheese", "Grocery", 5m),
    ];

    [Fact]
    public void The_column_factory_fixes_the_row_type_so_a_cell_lambda_needs_no_annotation()
    {
        // `p` is a Product because the factory's parameter is the grid, and the grid's type argument was
        // fixed by Data. Written as a flat child this would be CS0411.
        var html = UiDataGrid.Data(Catalog)[c => [
            c.Field(p => p.Name).Title("Product"),
            c.Field(p => p.Price).Title("Price")
                .Cell(p => Span[p.Price.ToString("0.00", CultureInfo.InvariantCulture)]),
        ]].ToHtml();

        Assert.Contains("<th", html, StringComparison.Ordinal);
        Assert.Contains("Product", html, StringComparison.Ordinal);
        Assert.Contains("Anvil", html, StringComparison.Ordinal);
        Assert.Contains("30.00", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_column_reads_its_own_cells_without_a_value_delegate()
    {
        var html = Grid();

        foreach (var product in Catalog)
        {
            Assert.Contains(product.Name, html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_null_column_is_dropped_rather_than_rendered()
    {
        // One arm of a conditional — `admin ? column : null` — is the reason a column is a component.
        var html = UiDataGrid.Data(Catalog)[c => [
            c.Field(p => p.Name).Title("Product"),
            null,
        ]].ToHtml();

        Assert.Equal(1, Occurrences(html, "<th "));
    }

    [Fact]
    public void An_empty_set_says_so_across_every_column()
    {
        var html = UiDataGrid.Data(Array.Empty<Product>())[c => [
            c.Field(p => p.Name).Title("Product"),
            c.Field(p => p.Price).Title("Price"),
        ]].ToHtml();

        Assert.Contains("Nothing to show.", html, StringComparison.Ordinal);
        Assert.Contains("colspan=\"2\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_empty_component_replaces_the_default_words()
    {
        var html = UiDataGrid.Data(Array.Empty<Product>()).Empty(P["No products yet."])[c => [
            c.Field(p => p.Name).Title("Product"),
        ]].ToHtml();

        Assert.Contains("No products yet.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing to show.", html, StringComparison.Ordinal);
    }

    // ---- sorting --------------------------------------------------------------------------------

    [Fact]
    public void A_sortable_header_is_a_button_and_an_unsortable_one_is_not()
    {
        var html = UiDataGrid.Data(Catalog)[c => [
            c.Field(p => p.Name).Title("Product").Sortable(true),
            c.Field(p => p.Price).Title("Price"),
        ]].ToHtml();

        Assert.Equal(1, Occurrences(html, "<button"));
    }

    [Fact]
    public void A_sortable_column_with_no_field_gets_no_sort_control()
    {
        // It could not be sorted if it tried: the sort is carried as a field TOKEN, and a column with no
        // field has none. Offering the control anyway would be a button that does nothing.
        var html = UiDataGrid.Data(Catalog)[c => [
            c.Column().Title("Actions").Sortable(true).Cell(_ => Span["edit"]),
        ]].ToHtml();

        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_sortable_header_announces_its_sort_state()
    {
        var html = UiDataGrid.Data(Catalog).Sort("name")[c => [
            c.Field(p => p.Name).Title("Product").Sortable(true),
            c.Field(p => p.Price).Title("Price").Sortable(true),
        ]].ToHtml();

        Assert.Contains("aria-sort=\"ascending\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-sort=\"none\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_controlled_descending_sort_says_descending()
    {
        var html = UiDataGrid.Data(Catalog).Sort("name").SortDescending(true)[c => [
            c.Field(p => p.Name).Title("Product").Sortable(true),
        ]].ToHtml();

        Assert.Contains("aria-sort=\"descending\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_uncontrolled_sort_orders_the_rows_in_memory()
    {
        var grid = (UiDataGrid<Product>)UiDataGrid.Data(Catalog)[c => [
            c.Field(p => p.Name).Title("Product").Sortable(true),
        ]];

        // Unsorted, the rows keep the order they were given.
        Assert.True(Position(grid.ToHtml(), "Anvil") < Position(grid.ToHtml(), "Bread"));

        var sorted = ((UiDataGrid<Product>)UiDataGrid.Data(Catalog).Sort("price")[c => [
            c.Field(p => p.Name).Title("Product"),
            c.Field(p => p.Price).Title("Price").Sortable(true),
        ]]).ToHtml();

        // Cheapest first: Bread at 2 comes before Anvil at 30.
        Assert.True(Position(sorted, "Bread") < Position(sorted, "Anvil"));
    }

    // ---- paging ---------------------------------------------------------------------------------

    [Fact]
    public void Paging_slices_the_rows_and_counts_the_pages()
    {
        var html = UiDataGrid.Data(Catalog).PageSize(2)[c => [
            c.Field(p => p.Name).Title("Product"),
        ]].ToHtml();

        Assert.Contains("Anvil", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Bread", html, StringComparison.Ordinal);
        Assert.Contains("4 rows", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_controlled_page_shows_that_page()
    {
        var html = UiDataGrid.Data(Catalog).PageSize(2).Page(1).OnPageChange(_ => { })[c => [
            c.Field(p => p.Name).Title("Product"),
        ]].ToHtml();

        Assert.DoesNotContain("Anvil", html, StringComparison.Ordinal);
        Assert.Contains("Bread", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pre_sliced_page_is_rendered_as_given_and_counted_by_TotalCount()
    {
        // The parent fetched one page itself. The grid must not slice it again — doing so would show two
        // of the ten rows it was handed — and the pager counts in what it was told.
        var html = UiDataGrid.Data(Catalog).PageSize(2).TotalCount(40)[c => [
            c.Field(p => p.Name).Title("Product"),
        ]].ToHtml();

        Assert.Contains("Anvil", html, StringComparison.Ordinal);
        Assert.Contains("Bread", html, StringComparison.Ordinal);
        Assert.Contains("40 rows", html, StringComparison.Ordinal);
    }

    [Fact]
    public void One_page_of_rows_needs_no_pager()
    {
        var html = UiDataGrid.Data(Catalog).PageSize(10)[c => [
            c.Field(p => p.Name).Title("Product"),
        ]].ToHtml();

        Assert.DoesNotContain("join", html, StringComparison.Ordinal);
    }

    // ---- what a field may name --------------------------------------------------------------------

    [Fact]
    public void A_field_may_walk_a_chain_of_members_and_is_named_by_the_last()
    {
        Order[] orders = [new(new Supplier("Acme"))];

        var html = UiDataGrid.Data(orders).Sort("name")[c => [
            c.Field(o => o.Supplier.Name).Title("Supplier").Sortable(true),
        ]].ToHtml();

        Assert.Contains("Acme", html, StringComparison.Ordinal);
        Assert.Contains("aria-sort=\"ascending\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_partway_along_the_chain_renders_an_empty_cell_rather_than_throwing()
    {
        Order[] orders = [new(null!)];

        var html = UiDataGrid.Data(orders)[c => [
            c.Field(o => o.Supplier.Name).Title("Supplier"),
        ]].ToHtml();

        Assert.Contains("<td", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_computed_field_is_refused_and_says_which_step_was_wanted()
    {
        // Field is the column's identity as well as its value, so a computed one could be neither
        // sorted nor grouped and would render blank. Refusing it names the step that was meant.
        var error = Assert.Throws<ArgumentException>(() =>
            UiDataGrid.Data(Catalog)[c => [
                c.Field(p => p.Price * 2).Title("Double"),
            ]].ToHtml());

        Assert.Contains("Value", error.Message, StringComparison.Ordinal);
        Assert.Contains("Cell", error.Message, StringComparison.Ordinal);
    }

    private sealed record Supplier(string Name);

    // The supplier is declared non-nullable and handed a null on purpose: the point of the test above is
    // what the grid does with a chain that breaks halfway, which a nullable declaration would have the
    // call site guard instead.
    private sealed record Order(Supplier Supplier);

    // ---- the query path -------------------------------------------------------------------------

    [Fact]
    public void A_query_is_ordered_and_sliced_by_the_provider()
    {
        var html = UiDataGrid.Data(Catalog.AsQueryable()).PageSize(2).Sort("price")[c => [
            c.Field(p => p.Name).Title("Product"),
            c.Field(p => p.Price).Title("Price").Sortable(true),
        ]].ToHtml();

        // Cheapest two, in order, and nothing else: the ORDER BY and the Skip/Take both went to the
        // provider rather than being applied to an already-materialised list.
        Assert.Contains("Bread", html, StringComparison.Ordinal);
        Assert.Contains("Cheese", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Anvil", html, StringComparison.Ordinal);
        Assert.Contains("4 rows", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_grouped_query_orders_by_the_group_key_so_a_band_opens_once()
    {
        // The rows come back in id order, which interleaves the categories. Without the group key
        // leading the provider's ORDER BY, each band would open twice.
        var html = UiDataGrid.Data(Interleaved.AsQueryable())
            .Grouped(["category"])
            .OnGroupedChange(_ => { })[c => [
                c.Field(p => p.Category).Title("Category").Groupable(true),
                c.Field(p => p.Name).Title("Product"),
            ]].ToHtml();

        Assert.Equal(1, Occurrences(html, "Category: Hardware (2)"));
        Assert.Equal(1, Occurrences(html, "Category: Grocery (2)"));
    }

    private static readonly Product[] Interleaved =
    [
        new(1, "Anvil", "Hardware", 30m),
        new(2, "Bread", "Grocery", 2m),
        new(3, "Rope", "Hardware", 8m),
        new(4, "Cheese", "Grocery", 5m),
    ];

    // ---- footers --------------------------------------------------------------------------------

    [Fact]
    public void A_footer_totals_over_every_row_rather_than_the_page()
    {
        var html = UiDataGrid.Data(Catalog).PageSize(2)[c => [
            c.Field(p => p.Name).Title("Product"),
            c.Field(p => p.Price).Title("Price").Footer(rows => rows.Sum(r => r.Price)),
        ]].ToHtml();

        Assert.Contains("<tfoot", html, StringComparison.Ordinal);
        Assert.Contains("45", html, StringComparison.Ordinal);
    }

    [Fact]
    public void No_column_with_a_footer_means_no_tfoot_at_all()
    {
        Assert.DoesNotContain("<tfoot", Grid(), StringComparison.Ordinal);
    }

    // ---- the card layout ------------------------------------------------------------------------

    [Fact]
    public void Every_cell_carries_its_column_title_for_the_stacked_layout()
    {
        // The card layout below sm is the SAME cells restyled, and the label in front of each one comes
        // from this attribute through `before:content-[attr(data-label)]`. Without it the stacked rows
        // are values with nothing saying what they are.
        var html = Grid();

        Assert.Contains("data-label=\"Product\"", html, StringComparison.Ordinal);
        Assert.Contains("max-sm:before:content-[attr(data-label)]", html, StringComparison.Ordinal);
        Assert.Contains("max-sm:hidden", html, StringComparison.Ordinal);

        // Every responsive class is a max-sm: VARIANT, never a base utility with an sm: override. The
        // kit's sheet and the consuming app's are separate <link>s whose layers do not merge, so an app
        // that writes `hidden` anywhere emits an unconditional .hidden that lands later in the cascade
        // and hides the header at EVERY width. This assertion is what noticed.
        Assert.DoesNotContain("\"hidden sm:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("sm:table-header-group", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_authored_card_replaces_the_stacked_layout_rather_than_joining_it()
    {
        // Two layouts now exist, so the labels the stacked one needed would be dead weight — and the
        // table hides below sm instead of restyling.
        var html = UiDataGrid.Data(Catalog).Card(p => Span[p.Name])[c => [
            c.Field(p => p.Name).Title("Product"),
        ]].ToHtml();

        Assert.DoesNotContain("data-label", html, StringComparison.Ordinal);
        Assert.Contains("max-sm:hidden", html, StringComparison.Ordinal);
        Assert.Contains("sm:hidden", html, StringComparison.Ordinal);
    }

    // ---- selection ------------------------------------------------------------------------------

    [Fact]
    public void Selection_appears_only_once_a_row_key_and_a_selection_are_named()
    {
        // RowKey alone is an identity, not an invitation to select: the checkboxes arrive with the
        // selection itself.
        var keyed = UiDataGrid.Data(Catalog).RowKey(p => p.Id)[c => [
            c.Field(p => p.Name).Title("Product"),
        ]].ToHtml();

        Assert.DoesNotContain("checkbox", keyed, StringComparison.Ordinal);

        var selectable = UiDataGrid.Data(Catalog)
            .RowKey(p => p.Id)
            .Selected([2])[c => [
                c.Field(p => p.Name).Title("Product"),
            ]].ToHtml();

        Assert.Contains("checkbox", selectable, StringComparison.Ordinal);
        Assert.Contains("Select all rows on this page", selectable, StringComparison.Ordinal);
    }

    [Fact]
    public void The_selected_rows_are_the_ones_whose_keys_were_named()
    {
        var html = UiDataGrid.Data(Catalog)
            .RowKey(p => p.Id)
            .Selected([1, 3])[c => [
                c.Field(p => p.Name).Title("Product"),
            ]].ToHtml();

        // Two ticked rows, and the select-all box is not one of them: only two of four are selected.
        Assert.Equal(2, Occurrences(html, "checked"));
    }

    [Fact]
    public void Selecting_every_row_on_the_page_ticks_the_header_box_too()
    {
        var html = UiDataGrid.Data(Catalog)
            .RowKey(p => p.Id)
            .Selected([1, 2, 3, 4])[c => [
                c.Field(p => p.Name).Title("Product"),
            ]].ToHtml();

        Assert.Equal(5, Occurrences(html, "checked"));
    }

    // ---- detail rows ----------------------------------------------------------------------------

    [Fact]
    public void A_row_with_detail_gets_an_expander_and_the_detail_stays_shut()
    {
        var html = UiDataGrid.Data(Catalog).Detail(p => P[p.Category])[c => [
            c.Field(p => p.Name).Title("Product"),
        ]].ToHtml();

        Assert.Contains("Expand row", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Hardware", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_whose_detail_is_null_gets_no_expander()
    {
        var html = UiDataGrid.Data(Catalog)
            .Detail(p => p.Category == "Grocery" ? P["fresh"] : null)[c => [
                c.Field(p => p.Name).Title("Product"),
            ]].ToHtml();

        Assert.Equal(2, Occurrences(html, "Expand row"));
    }

    // ---- grouping -------------------------------------------------------------------------------

    [Fact]
    public void Grouping_bands_the_rows_and_takes_the_grouped_column_out_of_the_table()
    {
        var html = UiDataGrid.Data(Catalog).Grouped(["category"]).OnGroupedChange(_ => { })[c => [
            c.Field(p => p.Category).Title("Category").Groupable(true),
            c.Field(p => p.Name).Title("Product"),
        ]].ToHtml();

        // One band heading per category, and the column itself is gone: it would be one repeated word.
        Assert.Contains("Category: Hardware (2)", html, StringComparison.Ordinal);
        Assert.Contains("Category: Grocery (2)", html, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(html, "<th "));
    }

    [Fact]
    public void A_grouped_column_can_be_kept_in_the_table()
    {
        var html = UiDataGrid.Data(Catalog)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })
            .ShowGroupedColumns(true)[c => [
                c.Field(p => p.Category).Title("Category").Groupable(true),
                c.Field(p => p.Name).Title("Product"),
            ]].ToHtml();

        Assert.Equal(2, Occurrences(html, "<th "));
    }

    [Fact]
    public void Subtotals_repeat_a_column_footer_per_band()
    {
        var html = UiDataGrid.Data(Catalog)
            .Grouped(["category"])
            .OnGroupedChange(_ => { })
            .GroupSubtotals(true)[c => [
                c.Field(p => p.Category).Title("Category").Groupable(true),
                c.Field(p => p.Price).Title("Price").Footer(rows => rows.Sum(r => r.Price)),
            ]].ToHtml();

        // Hardware totals 38 and Grocery 7, and the grand total is still 45 in the foot.
        Assert.Contains("38", html, StringComparison.Ordinal);
        Assert.Contains("7", html, StringComparison.Ordinal);
        Assert.Contains("45", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_grouping_levels_nest_and_each_subtotal_covers_its_own_band()
    {
        // The case a single grouping level cannot expose: the OUTER subtotal has to span every inner
        // band beneath it, while the inner one spans only its own run. A walk carrying one "where did
        // this band start" index reports the outer total as the last inner band's — 30 here, not 38.
        var html = UiDataGrid.Data(Catalog)
            .Grouped(["category", "name"])
            .OnGroupedChange(_ => { })
            .GroupSubtotals(true)[c => [
                c.Field(p => p.Category).Title("Category").Groupable(true),
                c.Field(p => p.Name).Title("Product").Groupable(true),
                c.Field(p => p.Price).Title("Price").Footer(rows => rows.Sum(r => r.Price)),
            ]].ToHtml();

        // Outer bands, inner bands, and the outer totals: Hardware is 30 + 8 = 38, Grocery 2 + 5 = 7.
        Assert.Contains("Category: Hardware (2)", html, StringComparison.Ordinal);
        Assert.Contains("Product: Anvil (1)", html, StringComparison.Ordinal);
        Assert.Contains("Product: Rope (1)", html, StringComparison.Ordinal);
        Assert.Contains(">38<", html, StringComparison.Ordinal);
        Assert.Contains(">7<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inner_key_shared_across_two_outer_bands_stays_two_bands()
    {
        // Bristol appears under both counties. A walk that compared only the inner key against the
        // previous row would merge them into one band the moment they landed adjacent.
        Place[] places = [new("Avon", "Bristol"), new("Wiltshire", "Bristol")];

        var html = UiDataGrid.Data(places)
            .Grouped(["county", "town"])
            .OnGroupedChange(_ => { })[c => [
                c.Field(p => p.County).Title("County").Groupable(true),
                c.Field(p => p.Town).Title("Town").Groupable(true),
            ]].ToHtml();

        Assert.Equal(2, Occurrences(html, "Town: Bristol (1)"));
    }

    private sealed record Place(string County, string Town);

    [Fact]
    public void A_groupable_column_offers_a_group_button_in_its_header()
    {
        var html = UiDataGrid.Data(Catalog)[c => [
            c.Field(p => p.Category).Title("Category").Groupable(true),
            c.Field(p => p.Name).Title("Product"),
        ]].ToHtml();

        Assert.Contains("Group by Category", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Group by Product", html, StringComparison.Ordinal);
    }

    // ---- the column chooser ---------------------------------------------------------------------

    [Fact]
    public void A_hidden_column_leaves_the_table()
    {
        var html = UiDataGrid.Data(Catalog)
            .HiddenColumns(["price"])
            .OnHiddenColumnsChange(_ => { })[c => [
                c.Field(p => p.Name).Title("Product"),
                c.Field(p => p.Price).Title("Price"),
            ]].ToHtml();

        Assert.Contains("Product", html, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(html, "<th "));
    }

    [Fact]
    public void The_column_order_puts_the_named_columns_first()
    {
        var html = UiDataGrid.Data(Catalog)
            .ColumnOrder(["price"])
            .OnColumnOrderChange(_ => { })[c => [
                c.Field(p => p.Name).Title("Product"),
                c.Field(p => p.Price).Title("Price"),
            ]].ToHtml();

        Assert.True(Position(html, "Price") < Position(html, "Product"));
    }

    [Fact]
    public void The_chooser_is_shut_until_it_is_opened()
    {
        var html = UiDataGrid.Data(Catalog).ColumnChooser(true)[c => [
            c.Field(p => p.Name).Title("Product"),
        ]].ToHtml();

        Assert.Contains("Columns", html, StringComparison.Ordinal);
        Assert.DoesNotContain("checkbox-xs", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_group_panel_says_what_to_do_when_nothing_is_grouped()
    {
        var html = UiDataGrid.Data(Catalog).GroupPanel(true)[c => [
            c.Field(p => p.Category).Title("Category").Groupable(true),
        ]].ToHtml();

        Assert.Contains("Group by a column with its header button.", html, StringComparison.Ordinal);
    }

    // ---- presentation ---------------------------------------------------------------------------

    [Fact]
    public void A_busy_grid_announces_itself_rather_than_only_dimming()
    {
        var html = UiDataGrid.Data(Catalog).Loading(true)[c => [
            c.Field(p => p.Name).Title("Product"),
        ]].ToHtml();

        Assert.Contains("aria-busy=\"true\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_grid_keeps_its_name_while_it_is_busy()
    {
        // The two live in one aria bag, and a second Aria call would have replaced the first outright —
        // which is exactly what RASK044 reports.
        var html = UiDataGrid.Data(Catalog).Label("Catalog").Loading(true)[c => [
            c.Field(p => p.Name).Title("Product"),
        ]].ToHtml();

        Assert.Contains("aria-busy=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Catalog\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(UiSize.Xs, "table-xs")]
    [InlineData(UiSize.Lg, "table-lg")]
    public void Every_size_writes_its_own_class(UiSize size, string expected)
    {
        var html = UiDataGrid.Data(Catalog).Size(size)[c => [
            c.Field(p => p.Name).Title("Product"),
        ]].ToHtml();

        Assert.Contains(expected, html, StringComparison.Ordinal);
    }

    [Fact]
    public void Zebra_and_a_sticky_header_write_daisy_classes()
    {
        var html = UiDataGrid.Data(Catalog).Zebra(true).StickyHeader(true).MaxHeight("20rem")[c => [
            c.Field(p => p.Name).Title("Product"),
        ]].ToHtml();

        Assert.Contains("table-zebra", html, StringComparison.Ordinal);
        Assert.Contains("table-pin-rows", html, StringComparison.Ordinal);
        Assert.Contains("max-height:20rem", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_class_reaches_the_row_it_was_computed_from()
    {
        var html = UiDataGrid.Data(Catalog).RowClass(p => p.Price > 10m ? "text-error" : null)[c => [
            c.Field(p => p.Name).Title("Product"),
        ]].ToHtml();

        Assert.Equal(1, Occurrences(html, "text-error"));
    }

    // ---- the row-click safety rule ----------------------------------------------------------------

    [Fact]
    public void A_text_column_is_clickable_and_a_custom_cell_is_not()
    {
        // The asymmetry is a safety rule: the client cancels the default action of a click it dispatches,
        // so a link or a button inside a clickable cell would silently stop working.
        var html = UiDataGrid.Data(Catalog).OnRowClick(_ => { })[c => [
            c.Field(p => p.Name).Title("Product"),
            c.Field(p => p.Price).Title("Price").Cell(p => A.Href("/x")[$"{p.Price}"]),
        ]].ToHtml();

        Assert.Equal(4, Occurrences(html, "cursor-pointer"));
    }

    [Fact]
    public void A_custom_cell_can_opt_back_in()
    {
        var html = UiDataGrid.Data(Catalog).OnRowClick(_ => { })[c => [
            c.Field(p => p.Name).Title("Product").Cell(p => Span[p.Name]).RowClickable(true),
        ]].ToHtml();

        Assert.Equal(4, Occurrences(html, "cursor-pointer"));
    }

    [Fact]
    public void No_row_click_handler_means_no_clickable_cells()
    {
        Assert.DoesNotContain("cursor-pointer", Grid(), StringComparison.Ordinal);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static int Position(string haystack, string needle)
    {
        var at = haystack.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(at >= 0, $"not found: {needle}");
        return at;
    }

    // "<th " with the space: "<th" alone also matches "<thead", which counted one phantom column
    // in every assertion about how many there are.
    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }

    private static string Grid() =>
        UiDataGrid.Data(Catalog)[c => [
            c.Field(p => p.Name).Title("Product"),
            c.Field(p => p.Price).Title("Price"),
        ]].ToHtml();
}
