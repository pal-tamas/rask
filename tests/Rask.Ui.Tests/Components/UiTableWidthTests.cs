using Rask.Core;

namespace Rask.UiTests.Components;

/// <summary>
///     <see cref="UiTableColumn.Width" /> and <see cref="UiTableCell.Cut" />: a list whose columns hold still while its rows
///     change, and a long value cut short with the whole of it in its tooltip.
/// </summary>
public partial class UiTableWidthTests : RaskMarkup
{
    private static Component Suppliers(string? style = null) =>
        Ui.Table.Style(style)[
            Ui.TableColumns[
                Ui.TableColumn.Width(Ui.ColumnWidth.Rest)["Supplier"],
                Ui.TableColumn.Width(Ui.ColumnWidth.Day)["Since"],
                Ui.TableColumn.Width(Ui.ColumnWidth.Number)["Balance"],
                Ui.TableColumn.Width(Ui.ColumnWidth.Name)["Contact"],
                Ui.TableColumn.Width(Ui.ColumnWidth.RowMenu)
            ],
            Ui.TableRows[Ui.TableRow[Ui.TableCell.Cut("Aminoff Freight"), Ui.TableCell["2026. 01. 15."], Ui.TableCell.End["49"], Ui.TableCell["Lindsey"], Ui.TableCell]]
        ];

    [Fact]
    public void A_heading_is_as_wide_as_what_its_column_holds_and_its_label_wraps()
    {
        var html = Suppliers().ToHtml().AsText();

        Assert.Contains("<th class=\"[:where(&)]:w-28 print:w-auto whitespace-normal hyphens-auto break-words\" data-ui-column>", html, StringComparison.Ordinal);
        Assert.Contains("[:where(&)]:w-72 print:w-auto", html, StringComparison.Ordinal);
        Assert.Contains("<th class=\"print:min-w-60 print:w-auto whitespace-normal hyphens-auto break-words\" data-ui-column>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_number_and_the_row_actions_head_their_columns_end_unless_the_page_says_otherwise()
    {
        var number = Ui.TableColumn.Width(Ui.ColumnWidth.Number)["Balance"].ToHtml().AsText();
        var actions = Ui.TableColumn.Width(Ui.ColumnWidth.Buttons).ToHtml().AsText();
        var told = Ui.TableColumn.Width(Ui.ColumnWidth.Number).Align(Ui.Align.Start)["Balance"].ToHtml().AsText();

        Assert.StartsWith("<th class=\"group/end-align [:where(&)]:w-28", number, StringComparison.Ordinal);
        Assert.StartsWith("<th class=\"group/end-align [:where(&)]:w-48", actions, StringComparison.Ordinal);
        Assert.StartsWith("<th class=\"[:where(&)]:w-28", told, StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_whose_headings_name_widths_fills_its_box_and_is_never_narrower_than_they_take_together()
    {
        var html = Suppliers(style: "max-height: 20rem;").ToHtml().AsText();

        // 12 + 7 + 7 + 18 + 4 rem.
        Assert.StartsWith("<div class=\"flex flex-col *:data-ui-pagination:shrink-0 min-w-0\">", html, StringComparison.Ordinal);
        Assert.Matches("<table class=\"[^\"]* \\[:where\\(&\\)\\]:w-full min-w-\\(--ui-table-least\\) print:min-w-0\" style=\"max-height: 20rem; --ui-table-least: 48rem\"", html);
    }

    [Fact]
    public void Every_width_but_code_is_a_step_and_a_short_code_takes_ten_rem()
    {
        // `Code` is a step too, but the <code> element every component inherits hides it: `.Width(Ui.ColumnWidth.Code)`.
        Component[] headings =
        [
            Ui.TableColumn.Rest, Ui.TableColumn.Day, Ui.TableColumn.Moment, Ui.TableColumn.Number, Ui.TableColumn.Short,
            Ui.TableColumn.Flag, Ui.TableColumn.Reference, Ui.TableColumn.Name, Ui.TableColumn.Actions, Ui.TableColumn.Buttons,
            Ui.TableColumn.RowMenu,
        ];

        var html = Ui.Table[Ui.TableColumns[Ui.TableColumn.Short["VAT number"], Ui.TableColumn.Reference["City"]]].ToHtml();

        Assert.Equal(Enum.GetValues<Ui.ColumnWidth>().Length - 1, headings.Length);
        Assert.Contains("style=\"--ui-table-least: 40rem\"", html, StringComparison.Ordinal);
        Assert.Equal(2, html.Split("[:where(&amp;)]:w-40").Length - 1);
    }

    [Fact]
    public void An_actions_column_holds_two_icon_buttons_in_six_rem_at_the_end_and_is_named_to_a_screen_reader()
    {
        var table = Ui.Table[Ui.TableColumns[Ui.TableColumn.Rest["Town"], Ui.TableColumn.Number["Depots"], Ui.TableColumn.Actions]];

        var html = table.ToHtml().AsText();

        // 12 + 7 + 6 rem, under the forty a table never goes below; the heading ends its column and is not held at the edge.
        Assert.Contains("style=\"--ui-table-least: 40rem\"", html, StringComparison.Ordinal);
        Assert.Contains("<th class=\"group/end-align [:where(&)]:w-24 print:w-auto whitespace-normal hyphens-auto break-words\" data-ui-column>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"sr-only\">Actions</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("sticky", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Seven_actions_columns_count_six_rem_each_towards_the_table_s_least_width()
    {
        var table = Ui.Table[Ui.TableColumns[Enumerable.Range(0, 7).Select(_ => Ui.TableColumn.Actions)]];

        var html = table.ToHtml();

        Assert.Contains("style=\"--ui-table-least: 42rem\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_code_column_is_eight_rem_wide_at_its_start()
    {
        var heading = Ui.TableColumn.Width(Ui.ColumnWidth.Code)["Invoice"];

        var html = heading.ToHtml().AsText();

        Assert.Equal("<th class=\"[:where(&)]:w-32 print:w-auto whitespace-normal hyphens-auto break-words\" data-ui-column><div class=\"flex in-[.group\\/center-align]:justify-center in-[.group\\/end-align]:justify-end\">Invoice</div></th>", html);
    }

    [Fact]
    public void The_least_width_counts_each_column_as_wide_as_it_really_is()
    {
        // Eszaf's ListTable: a code and two buttons sized by the app's own class beside the kit's widths.
        var table = Ui.Table[Ui.TableColumns[
            Ui.TableColumn.Day["Day"], Ui.TableColumn.Name["Partner"], Ui.TableColumn.Class("w-32")["Code"],
            Ui.TableColumn.Class("w-24 print:w-auto"), Ui.TableColumn.Width(Ui.ColumnWidth.Code)["Plate"], Ui.TableColumn["Note"]]];

        var html = table.ToHtml();

        // 7 + 18 + 8 + 6 + 8 + 12 rem; the code and the buttons were counted as two rests (12 each) before.
        Assert.Contains("style=\"--ui-table-least: 59rem\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_column_s_own_class_wins_over_its_kit_width_and_a_width_under_a_variant_is_not_counted()
    {
        var table = Ui.Table[Ui.TableColumns[
            Ui.TableColumn.Name.Class("w-52")["Partner"], Ui.TableColumn.Class("w-[9.5rem]")["Since"],
            Ui.TableColumn.Class("min-w-[200px] w-20")["Town"], Ui.TableColumn.Class("sm:w-96 print:min-w-60")["Note"]]];

        var html = table.ToHtml();

        // 13 + 9.5 + 12.5 (the larger of 200px and 5rem) + 12 (the rest's) rem.
        Assert.Contains("style=\"--ui-table-least: 47rem\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_least_width_of_a_fraction_of_a_rem_is_written_as_one()
    {
        var table = Ui.Table[Ui.TableColumns[
            Ui.TableColumn.Name["Partner"], Ui.TableColumn.Name["Contact"], Ui.TableColumn.Width(Ui.ColumnWidth.Code).Class("w-[8.5rem]")["Code"]]];

        var html = table.ToHtml();

        Assert.Contains("style=\"--ui-table-least: 44.5rem\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_of_few_narrow_columns_is_still_forty_rem_wide_at_least()
    {
        var table = Ui.Table[Ui.TableColumns[Ui.TableColumn.Width(Ui.ColumnWidth.Day)["Day"], Ui.TableColumn["Note"]]];

        var html = table.ToHtml();

        Assert.Contains("style=\"--ui-table-least: 40rem\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_whose_headings_name_no_width_is_flux_s_sized_by_what_it_holds()
    {
        var table = Ui.Table[Ui.TableColumns[Ui.TableColumn["Customer"], Ui.TableColumn["Amount"]]];

        var html = table.ToHtml();

        Assert.DoesNotContain("--ui-table-least", html, StringComparison.Ordinal);
        Assert.StartsWith("<div class=\"flex flex-col *:data-ui-pagination:shrink-0\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_row_actions_heading_given_no_words_names_its_column_to_a_screen_reader()
    {
        var english = Ui.TableColumn.Width(Ui.ColumnWidth.RowMenu).ToHtml();
        var hungarian = KitCulture.In("hu-HU", () => Ui.TableColumn.Width(Ui.ColumnWidth.Buttons).ToHtml().AsText());
        var worded = Ui.TableColumn.Width(Ui.ColumnWidth.Buttons)["Do"].ToHtml();

        Assert.Contains("<span class=\"sr-only\">Actions</span>", english, StringComparison.Ordinal);
        Assert.Contains("<span class=\"sr-only\">Műveletek</span>", hungarian, StringComparison.Ordinal);
        Assert.DoesNotContain("sr-only", worded, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cut_cell_shows_its_value_on_one_line_with_the_whole_of_it_in_its_title()
    {
        var cell = Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Cut("Aminoff Freight & Haulage");

        var html = cell.ToHtml();

        Assert.Equal("<td class=\"font-medium text-zinc-800 dark:text-white truncate\" title=\"Aminoff Freight &amp; Haulage\" data-ui-cell>Aminoff Freight &amp; Haulage</td>", html);
    }

    [Fact]
    public void A_cut_cell_given_children_shows_them_and_the_page_s_own_title_wins()
    {
        var linked = Ui.TableCell.Cut("Aminoff Freight")[A.Href("/suppliers/1")["Aminoff Freight"]].ToHtml();
        var titled = Ui.TableCell.Cut("Aminoff Freight").Title("Supplier since 2019").ToHtml();

        Assert.Equal("<td class=\"truncate\" title=\"Aminoff Freight\" data-ui-cell><a href=\"/suppliers/1\">Aminoff Freight</a></td>", linked);
        Assert.Contains("title=\"Supplier since 2019\"", titled, StringComparison.Ordinal);
        Assert.DoesNotContain("title=\"Aminoff Freight\"", titled, StringComparison.Ordinal);
    }

    [Fact]
    public void A_whole_cell_keeps_its_value_on_one_line_and_is_marked_for_paper()
    {
        var cell = Ui.TableCell.Whole()["2026. 01. 15."];

        var html = cell.ToHtml();

        Assert.Equal("<td class=\"whitespace-nowrap\" data-ui-cell data-ui-whole>2026. 01. 15.</td>", html);
    }

    [Fact]
    public void A_cut_cell_kept_whole_is_cut_short_on_screen_and_keeps_its_title_and_the_call_site_s_data()
    {
        var cell = Ui.TableCell.End.Cut("2026. 01. 15. 13:45").Whole().Data("testid", "day");

        var html = cell.ToHtml();

        Assert.Equal(
            "<td class=\"text-end [&amp;&gt;*]:ms-auto truncate whitespace-nowrap\" title=\"2026. 01. 15. 13:45\" data-ui-cell data-ui-whole data-testid=\"day\">2026. 01. 15. 13:45</td>",
            html);
    }

    [Fact]
    public void A_cell_told_not_to_be_whole_wraps_as_any_other()
    {
        var cell = Ui.TableCell.Whole(false).Cut("Aminoff Freight");

        var html = cell.ToHtml();

        Assert.Equal("<td class=\"truncate\" title=\"Aminoff Freight\" data-ui-cell>Aminoff Freight</td>", html);
    }

    [Fact]
    public void A_plain_cell_still_writes_no_class()
    {
        var html = Ui.TableCell["Lindsey"].ToHtml();

        Assert.Equal("<td data-ui-cell>Lindsey</td>", html);
    }
}
