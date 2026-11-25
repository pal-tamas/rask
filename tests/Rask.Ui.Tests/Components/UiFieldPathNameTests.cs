using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     A control bound to a member of a value object or a row is named by the member's whole path from the form's
///     model — the path its refusals are told by — and its id is drawn from that path, so it never clashes with a
///     member of the same name beside it.
/// </summary>
public partial class UiFieldPathNameTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_value_objects_member_posts_under_its_whole_path_beside_a_member_of_its_own_name()
    {
        var model = new Supply();

        var page = Page.Render(() => Form.Model(model)[
            Ui.Input.Bind(() => model.Quantity).Label("Quantity"),
            Ui.Input.Bind(() => model.Opening.Quantity).Label("Opening quantity"),
            Ui.Input.Bind(() => model.Opening.Note).Label("Opening note")
        ]);

        var inputs = page.FindAll("input[data-ui-control]");
        Assert.Equal(["Quantity", "Opening.Quantity", "Opening.Note"], inputs.Select(i => i.Attribute("name")));
        Assert.Equal(["f-quantity", "f-opening-quantity", "f-opening-note"], inputs.Select(i => i.Id));
    }

    [Fact]
    public void A_rows_member_posts_under_its_rows_place_and_its_value_objects_path()
    {
        var model = new Supply { Parts = [new Part(), new Part()] };

        var page = Page.Render(() => Form.Model(model)[
            model.Parts.Select(row => (Component)Ui.Input.Key(row).Bind(() => row.Start.Quantity).Label("Start"))
        ]);

        var inputs = page.FindAll("input[data-ui-control]");
        Assert.Equal(["Parts[0].Start.Quantity", "Parts[1].Start.Quantity"], inputs.Select(i => i.Attribute("name")));
        Assert.Equal(["f-start-quantity", "f-start-quantity-2"], inputs.Select(i => i.Id));
    }

    [Fact]
    public void A_bind_written_through_an_index_is_named_by_it_outside_a_form_too()
    {
        var model = new Supply { Parts = [new Part(), new Part()] };
        var i = 1;

        var page = Page.Render(() => (Component)Div[Ui.Input.Bind(() => model.Parts[i].Start.Quantity).Label("Second")]);

        var input = page.Find("input[data-ui-control]");
        Assert.Equal("Parts[1].Start.Quantity", input.Attribute("name"));
        Assert.Equal("f-parts-start-quantity", input.Id);
    }

    private sealed class Supply
    {
        public decimal? Quantity { get; set; }

        public Opening Opening { get; set; } = new();

        public List<Part> Parts { get; set; } = [];
    }

    private sealed class Opening
    {
        public decimal? Quantity { get; set; }

        public string? Note { get; set; }
    }

    private sealed class Part
    {
        public Opening Start { get; set; } = new();
    }
}
