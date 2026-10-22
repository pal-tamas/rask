using System.Text.RegularExpressions;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.Table.Loading(…)</c>: a table whose page loads its rows into a field of its own draws them as
///     <c>Ui.Table.Of(query)</c> draws a query's — skeleton rows while they are on their way, a row saying there is
///     nothing when there are none.
/// </summary>
public sealed partial class UiTableLoadingTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Rows_on_their_way_show_the_headings_and_a_skeleton_row_per_row_of_the_page()
    {
        string[]? rows = null;

        var html = Invoices(rows, total: 40).ToHtml();

        Assert.Contains("data-rask-skeleton=\"loading\"", html, StringComparison.Ordinal);
        Assert.Contains("<thead data-ui-columns>", html, StringComparison.Ordinal);
        Assert.Equal(5, Rows(html));
        Assert.Equal(5 * 3, Regex.Count(html, "data-ui-skeleton-line"));
    }

    [Fact]
    public void A_table_with_no_pager_shows_fifteen_skeleton_rows()
    {
        var table = Ui.Table.Loading(true)[Ui.TableColumns[Ui.TableColumn["Name"]], Ui.TableRows];

        var html = table.ToHtml();

        Assert.Equal(15, Rows(html));
    }

    [Fact]
    public void Rows_that_came_show_themselves_and_say_they_are_not_loading()
    {
        string[] rows = ["Lindsey", "Hanna", "Kianna"];

        var html = Invoices(rows, total: 40).ToHtml();

        Assert.Contains("data-rask-skeleton=\"\"", html, StringComparison.Ordinal);
        Assert.Equal(3, Rows(html));
        Assert.DoesNotContain("data-ui-skeleton-line", html, StringComparison.Ordinal);
    }

    [Fact]
    public void No_rows_say_so_in_one_row_across_every_column()
    {
        string[] rows = [];

        var html = Invoices(rows, total: 0).ToHtml();

        Assert.Equal(1, Rows(html));
        Assert.Matches("<td [^>]*colspan=\"3\"[^>]*>No results.</td>", html);
    }

    private static global::Rask.Core.Component Invoices(string[]? rows, int total) =>
        Ui.Table.Loading(rows is null).Paginate(Ui.Pagination.Paginator(new UiPaginator { Page = 1, PerPage = 5, Total = total }))[
            Ui.TableColumns[Ui.TableColumn["Customer"], Ui.TableColumn["Date"], Ui.TableColumn["Amount"]],
            Ui.TableRows[
                (rows ?? []).Select(name => Ui.TableRow.Key(name)[Ui.TableCell[name], Ui.TableCell["Today"], Ui.TableCell["$1"]])
            ]
        ];

    private static int Rows(string html) => Regex.Count(html[html.IndexOf("<tbody", StringComparison.Ordinal)..], "<tr[ >]");
}
