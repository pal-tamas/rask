using Rask.Core;

namespace Rask.UiTests.Components;

/// <summary>
///     <see cref="UiEmpty" />: what a list says when it has nothing to show, drawn as Flux's own demo draws it — a status
///     line, centred, 24px above and below, in the ink of the table cell it sits in.
/// </summary>
public partial class UiEmptyTests : RaskMarkup
{
    [Fact]
    public void It_is_a_status_line_centred_with_room_above_and_below_and_no_ink_of_its_own()
    {
        var empty = Ui.Empty["No suppliers yet."];

        var html = empty.ToHtml().AsText();

        Assert.Equal(
            "<div class=\"[:where(&)]:py-6 [:where(&)]:text-center\" role=\"status\">No suppliers yet.</div>",
            html);
    }

    [Fact]
    public void A_class_and_a_role_of_the_call_sites_are_its_own()
    {
        var empty = Ui.Empty.Class("py-12").Role("note")["Nothing archived."];

        var html = empty.ToHtml().AsText();

        Assert.Contains("[:where(&)]:text-center py-12\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"note\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"status\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void In_a_row_it_is_the_only_cell_spanning_every_column_as_flux_writes_it()
    {
        var row = Ui.TableRow[Ui.TableCell.ColSpan(3)[Ui.Empty["No matching transactions"]]];

        var html = row.ToHtml().AsText();

        Assert.Matches("^<tr data-ui-row><td data-ui-cell colspan=\"3\"><div [^>]*role=\"status\">No matching transactions</div></td></tr>$", html);
    }
}
