using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;
using Rask.Testing;
using Rask.Wire;

namespace Rask.UiTests.Components;

/// <summary>
///     A bare bound control draws neither a label nor a message, as Flux's does; in a table cell its message is a
///     <c>Ui.Error.For(() =&gt; row.X)</c> beside it, as Flux's <c>flux:error name</c> is: it shows that member's messages.
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
    public async Task An_error_for_the_member_beside_each_rows_control_says_its_message_after_a_refused_save()
    {
        Row[] rows = [new(), new()];
        var page = Page.Render(() => Form.Model(rows[0]).OnSubmit(_ => Task.FromException(new Refused("Too big.", "Size")))[
            rows.Select(row => Ui.TableCell.Key(row.Key)[Ui.Input.Bind(() => row.Size), Ui.Error.For(() => row.Size).Id("e-" + row.Key)]),
            Ui.Button.Submit["Save"]
        ]);

        await page.On("form").Submit();

        Assert.Contains("Too big.", page.TextOf("#e-" + rows[0].Key), StringComparison.Ordinal);
        Assert.True(page.Exists("#e-" + rows[1].Key + ".hidden"));
        Assert.Equal("true", page.Find("input#f-size").Attribute("aria-invalid"));
    }

    [Fact]
    public void An_error_beside_a_valid_control_is_hidden()
    {
        var row = new Row();

        var page = Page.Render(() => Form.Model(row)[Ui.Input.Bind(() => row.Size), Ui.Error.For(() => row.Size)]);

        Assert.True(page.Exists("[data-ui-error].hidden"));
        Assert.Null(page.Find("input#f-size").Attribute("aria-invalid"));
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
