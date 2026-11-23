using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;
using Rask.Testing;
using Rask.Wire;

namespace Rask.UiTests.Components;

/// <summary>
///     A bare bound control draws neither a label nor a message, as Flux's does; in a table cell its message is a
///     <c>Ui.Error.For(() =&gt; row.X)</c> beside it, which takes the id the control's <c>aria-describedby</c> names.
/// </summary>
public partial class UiErrorBesideTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_bare_bound_control_draws_no_label_and_no_message()
    {
        var row = new Row();

        var page = Page.Render(() => Form.Model(row)[Ui.TableCell[Ui.Input.Bind(() => row.Size)]]);

        Assert.False(page.Exists("label"));
        Assert.False(page.Exists("[data-ui-error]"));
        Assert.False(page.Exists("[data-ui-field]"));
    }

    [Fact]
    public async Task An_error_for_the_member_beside_the_control_says_its_message_after_a_refused_save()
    {
        var row = new Row();
        var page = Page.Render(() => Form.Model(row).OnSubmit(_ => Task.FromException(new Refused("Too big.", "Size")))[
            Ui.TableCell[Ui.Input.Bind(() => row.Size), Ui.Error.For(() => row.Size)],
            Ui.Button.Submit["Save"]
        ]);

        await page.On("form").Submit();

        Assert.Contains("Too big.", page.TextOf("#f-size-error"), StringComparison.Ordinal);
        Assert.Equal("true", page.Find("input#f-size").Attribute("aria-invalid"));
        Assert.Equal("f-size-error", page.Find("input#f-size").Attribute("aria-describedby"));
    }

    [Fact]
    public void Each_rows_error_takes_its_own_controls_id()
    {
        Row[] rows = [new(), new()];

        var page = Page.Render(() => Form.Model(rows[0])[
            rows.Select(row => Ui.TableCell.Key(row.Key)[Ui.Input.Bind(() => row.Size), Ui.Error.For(() => row.Size)])
        ]);

        Assert.Equal(2, page.FindAll("[data-ui-error]").Count);
        Assert.True(page.Exists("input#f-size"));
        Assert.True(page.Exists("input#f-size-2"));
        Assert.True(page.Exists("#f-size-error[data-ui-error]"));
        Assert.True(page.Exists("#f-size-2-error[data-ui-error]"));
    }

    [Fact]
    public void A_valid_control_names_no_error()
    {
        var row = new Row();

        var page = Page.Render(() => Form.Model(row)[Ui.Input.Bind(() => row.Size), Ui.Error.For(() => row.Size)]);

        Assert.Null(page.Find("input#f-size").Attribute("aria-describedby"));
        Assert.True(page.Exists("#f-size-error.hidden"));
    }

    private sealed class Row
    {
        public string Key { get; } = Guid.NewGuid().ToString();

        [Range(1, 10)]
        public int? Size { get; set; }
    }

    private sealed class Refused(string message, string field) : Exception("refused"), IFieldFailures
    {
        public IReadOnlyList<FieldFailure> Failures { get; } = [new FieldFailure(message, [field])];
    }
}
