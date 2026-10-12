using System.Text.RegularExpressions;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

// The orders table sorts, so it follows the kit guide's "Columns that stay put": the table has a width and
// every column but one has one, which is what keeps a sort from moving the columns.
public sealed partial class MasterDetailDemoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_orders_table_gives_every_column_but_one_a_width()
    {
        var html = Page.Render(() => MasterDetailDemo, TestServices.Default()).Html;

        var headings = Regex.Matches(html[..html.IndexOf("</thead>", StringComparison.Ordinal)], "<th[ >][^>]*");

        Assert.Matches("<table[^>]*id=\"md-orders\"[^>]*class=\"[^\"]*w-full min-w-190", html);
        Assert.Equal(6, headings.Count);
        Assert.Single(headings, heading => !Regex.IsMatch(heading.Value, @"[ ""]w-\d+"));
    }

    [Fact]
    public void A_long_customer_name_is_cut_at_its_column_and_kept_in_the_tooltip()
    {
        var html = Page.Render(() => MasterDetailDemo, TestServices.Default()).Html;

        var cell = Regex.Match(html, "<td[^>]*title=\"Ada Lovelace\"[^>]*>");

        Assert.True(cell.Success, "no cell carries the customer's name as its title");
        Assert.Contains("truncate", cell.Value, StringComparison.Ordinal);
    }
}
