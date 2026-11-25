using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;
using Rask.Testing;
using Rask.Wire;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.Field[Ui.Input.Bind(() =&gt; m.X)]</c> — a field holding a bound control and no <c>Ui.Label</c> — is labelled by
///     the member: its <c>[Display(Name)]</c>, the <em>Required</em> badge, its <c>[Display(Description)]</c>, wired to the
///     control by the field's ids. A field with a label of its own, or an unbound control, is drawn as it was.
/// </summary>
public partial class UiFieldNamesItsControlTests : global::Rask.Core.RaskMarkup
{
    static UiFieldNamesItsControlTests() =>
        RaskValidation.RegisterFieldShapes(typeof(Trip), static name => name switch
        {
            nameof(Trip.Km) => new FieldShape(FieldKind.Range, Min: 0, Start: "Start", End: "End", Ends: FieldKind.Integer),
            _ => null,
        });

    [Fact]
    public void A_field_with_no_label_is_labelled_by_its_controls_member()
    {
        var trip = new Trip();

        var page = Page.Render(() => Ui.Field[Ui.Input.Bind(() => trip.Plate).Class("uppercase")]);

        Assert.Equal("Rendszám", page.TextOf("label[for=\"f-plate\"]"));
        Assert.True(page.Exists("label#f-plate-label[data-ui-label]"));
        Assert.Contains("uppercase", page.Find("[data-ui-input]").Attribute("class"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_member_that_must_be_filled_badges_the_label_and_the_controls_badge_wins()
    {
        var trip = new Trip();

        var page = Page.Render(() => Div[
            Ui.Field[Ui.Input.Bind(() => trip.Driver)],
            Ui.Field[Ui.Select.Bind(() => trip.Driver).Badge("")[Ui.SelectOption.Value("Kovács")["Kovács"]]]
        ]);

        Assert.Equal("SofőrRequired", page.TextOf("label[for=\"f-driver\"]"));
        Assert.True(page.Exists("input#f-driver[aria-required=\"true\"]"));
        Assert.Equal("Sofőr", page.TextOf("#f-driver-2-label"));
    }

    [Fact]
    public void The_members_description_is_drawn_and_describes_the_control()
    {
        var trip = new Trip();

        var page = Page.Render(() => Ui.Field[Ui.Input.Bind(() => trip.Driver), Ui.Error]);

        Assert.Equal("Aki vezet", page.TextOf("#f-driver-description"));
        Assert.Contains("f-driver-description", page.Find("input#f-driver").Attribute("aria-describedby"), StringComparison.Ordinal);
        Assert.Equal(["f-driver-label", "f-driver-description", "f-driver-error"], Ordered(page.Html, "f-driver-label", "f-driver-description", "f-driver-error"));
        Assert.True(page.Html.IndexOf("id=\"f-driver-description\"", StringComparison.Ordinal) < page.Html.IndexOf("data-ui-input", StringComparison.Ordinal));
    }

    [Fact]
    public void A_description_of_the_fields_own_wins_over_the_members()
    {
        var trip = new Trip();

        var page = Page.Render(() => Ui.Field[Ui.Input.Bind(() => trip.Driver), Ui.Description["Saját"]]);

        Assert.Single(page.FindAll("[data-ui-description]"));
        Assert.Equal("Saját", page.TextOf("#f-driver-description"));
        Assert.Equal("SofőrRequired", page.TextOf("#f-driver-label"));
    }

    [Fact]
    public void A_field_with_a_label_of_its_own_is_drawn_as_before()
    {
        var trip = new Trip();
        var plain = new Plain();

        var named = Page.Render(() => Ui.Field[Ui.Label["Plate"], Ui.Input.Bind(() => trip.Plate), Ui.Error]).Html;
        var unnamed = Page.Render(() => Ui.Field[Ui.Label["Plate"], Ui.Input.Bind(() => plain.Plate), Ui.Error]).Html;

        Assert.Equal(unnamed, named);
        Assert.DoesNotContain("Rendszám", named, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_whose_control_is_not_bound_draws_no_label()
    {
        var plain = new Plain();

        var page = Page.Render(() => Div[
            Ui.Field[Ui.Input.Value("AB-123")],
            Ui.Field[Ui.Input.Bind(() => plain.Plate)]
        ]);

        Assert.False(page.Exists("label"));
        Assert.False(page.Exists("[data-ui-description]"));
    }

    [Fact]
    public void A_checkbox_comes_before_its_label_in_an_inline_field()
    {
        var trip = new Trip();

        var page = Page.Render(() => Ui.Field[Ui.Checkbox.Bind(() => trip.Loaded)]);

        Assert.True(page.Exists("[data-ui-field] > label#f-loaded-label[for=\"f-loaded\"]"));
        Assert.True(page.Html.IndexOf("data-ui-checkbox", StringComparison.Ordinal) < page.Html.IndexOf("id=\"f-loaded-label\"", StringComparison.Ordinal));
        Assert.Contains("grid", page.Find("[data-ui-field]").Attribute("class"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_ranges_ends_are_not_labelled_again()
    {
        var trip = new Trip();

        var page = Page.Render(() => Ui.Field.Bind(() => trip.Km));

        Assert.Single(page.FindAll("label"));
        Assert.Equal("Kilometres", page.TextOf("label[for=\"f-km-start\"]"));
    }

    [Fact]
    public async Task The_controls_own_message_is_said_under_a_field_that_labels_it()
    {
        var trip = new Trip { Driver = "Kovács" };
        var page = Page.Render(() => Form.Model(trip).OnSubmit(_ => Task.FromException(new Refused("Taken.", "Plate")))[
            Ui.Field[Ui.Input.Bind(() => trip.Plate), Ui.Error],
            Ui.Button.Submit["Save"]
        ]);

        await page.On("form").Submit();

        Assert.Equal("Taken.", page.TextOf("#f-plate-error"));
        Assert.Contains("f-plate-error", page.Find("input#f-plate").Attribute("aria-describedby"), StringComparison.Ordinal);
    }

    // The ids in the order the page writes them.
    private static string[] Ordered(string html, params string[] ids) =>
        [.. ids.OrderBy(id => html.IndexOf("id=\"" + id + "\"", StringComparison.Ordinal))];

    private sealed class Trip
    {
        [Required]
        [Display(Name = "Sofőr", Description = "Aki vezet")]
        public string Driver { get; set; } = "";

        [Display(Name = "Rendszám")]
        public string? Plate { get; set; }

        [Display(Name = "Megrakva")]
        public bool? Loaded { get; set; }

        [Display(Name = "Kilometres")]
        public Leg Km { get; set; } = new();
    }

    private sealed class Leg
    {
        [Display(Name = "From")]
        public int? Start { get; set; }

        [Display(Name = "To")]
        public int? End { get; set; }
    }

    private sealed class Plain
    {
        public string? Plate { get; set; }
    }

    private sealed class Refused(string message, string field) : Exception("refused"), IFieldFailures
    {
        public IReadOnlyList<FieldFailure> Failures { get; } = [new FieldFailure(message, [field])];
    }
}
