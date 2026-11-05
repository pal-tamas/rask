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
                Ui.TableColumn.Width(Ui.ColumnWidth.Actions)
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
        var actions = Ui.TableColumn.Width(Ui.ColumnWidth.Actions).ToHtml().AsText();
        var told = Ui.TableColumn.Width(Ui.ColumnWidth.Number).Align(Ui.Align.Start)["Balance"].ToHtml().AsText();

        Assert.StartsWith("<th class=\"group/end-align [:where(&)]:w-28", number, StringComparison.Ordinal);
        Assert.StartsWith("<th class=\"group/end-align [:where(&)]:w-24", actions, StringComparison.Ordinal);
        Assert.StartsWith("<th class=\"[:where(&)]:w-28", told, StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_whose_headings_name_widths_fills_its_box_and_is_never_narrower_than_they_take_together()
    {
        var html = Suppliers(style: "max-height: 20rem;").ToHtml().AsText();

        // 12 + 7 + 7 + 18 + 6 rem.
        Assert.StartsWith("<div class=\"flex flex-col *:data-ui-pagination:shrink-0 min-w-0\">", html, StringComparison.Ordinal);
        Assert.Matches("<table class=\"[^\"]* \\[:where\\(&\\)\\]:w-full min-w-\\(--ui-table-least\\) print:min-w-0\" style=\"max-height: 20rem; --ui-table-least: 50rem\"", html);
    }

    [Fact]
    public void Every_width_is_a_step_and_a_short_code_takes_ten_rem()
    {
        Component[] headings =
        [
            Ui.TableColumn.Rest, Ui.TableColumn.Day, Ui.TableColumn.Moment, Ui.TableColumn.Number, Ui.TableColumn.Short,
            Ui.TableColumn.Flag, Ui.TableColumn.Reference, Ui.TableColumn.Name, Ui.TableColumn.Actions, Ui.TableColumn.Buttons,
        ];

        var html = Ui.Table[Ui.TableColumns[Ui.TableColumn.Short["VAT number"], Ui.TableColumn.Reference["City"]]].ToHtml();

        Assert.Equal(10, headings.Length);
        Assert.Contains("style=\"--ui-table-least: 40rem\"", html, StringComparison.Ordinal);
        Assert.Equal(2, html.Split("[:where(&amp;)]:w-40").Length - 1);
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
        var english = Ui.TableColumn.Width(Ui.ColumnWidth.Actions).ToHtml();
        var hungarian = KitCulture.In("hu-HU", () => Ui.TableColumn.Width(Ui.ColumnWidth.Buttons).ToHtml().AsText());
        var worded = Ui.TableColumn.Width(Ui.ColumnWidth.Actions)["Do"].ToHtml();

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
    public void A_plain_cell_still_writes_no_class()
    {
        var html = Ui.TableCell["Lindsey"].ToHtml();

        Assert.Equal("<td data-ui-cell>Lindsey</td>", html);
    }
}
