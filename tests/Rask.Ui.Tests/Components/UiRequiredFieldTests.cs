using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;
using Rask.Testing;
using Rask.UiTests.Flux;

namespace Rask.UiTests.Components;

/// <summary>
///     A kit control bound to a field that must be filled says <c>aria-required</c>, on the element that carries
///     its other <c>aria-*</c>, whether the model's type registered the field or the member is <c>[Required]</c>.
/// </summary>
public partial class UiRequiredFieldTests : global::Rask.Core.RaskMarkup
{
    static UiRequiredFieldTests() => RaskValidation.RegisterRequiredFields(typeof(Trip), Trip.IsRequired);

    [Fact]
    public void A_text_input_says_aria_required_once_for_a_field_its_models_type_registered()
    {
        var trip = new Trip();

        var page = Page.Render(() => Ui.Input.Bind(() => trip.Name).Label("Name").Id("name"));

        Assert.True(page.Exists("input#name[aria-required=\"true\"]"));
        Assert.Single(page.FindAll("[aria-required]"));
        Assert.False(page.Exists("[required]"));
    }

    [Fact]
    public void A_textarea_says_aria_required_for_a_field_its_models_type_registered()
    {
        var trip = new Trip();

        var page = Page.Render(() => Ui.Textarea.Bind(() => trip.Name).Label("Name").Id("name"));

        Assert.True(page.Exists("textarea#name[aria-required=\"true\"]"));
        Assert.Single(page.FindAll("[aria-required]"));
    }

    [Fact]
    public void A_native_select_says_aria_required_for_a_field_its_models_type_registered()
    {
        var trip = new Trip();

        var page = Page.Render(() => Ui.Select.Bind(() => trip.Country).Label("Country").Id("country")[Countries()]);

        Assert.True(page.Exists("select#country[aria-required=\"true\"]"));
        Assert.Single(page.FindAll("[aria-required]"));
    }

    [Fact]
    public void A_listbox_select_says_aria_required_on_its_trigger_for_a_field_its_models_type_registered()
    {
        var trip = new Trip();

        var page = Page.Render(() =>
            Ui.Select.Bind(() => trip.Country).Variant(Ui.SelectVariant.Listbox).Label("Country").Id("country")[Countries()]);

        Assert.True(page.Exists("#country[aria-required=\"true\"]"));
        Assert.Single(page.FindAll("[aria-required]"));
    }

    [Fact]
    public void A_date_picker_says_aria_required_on_its_trigger_for_a_field_its_models_type_registered()
    {
        var trip = new Trip();

        var page = Page.Render(() =>
            Ui.DatePicker.Bind(() => trip.Leaves).Locale("en-US").On(new DateOnly(2026, 10, 9)).Label("Leaves").Id("leaves"));

        Assert.True(page.Exists("#leaves[role=\"combobox\"][aria-required=\"true\"]"));
        Assert.Single(page.FindAll("[aria-required]"));
    }

    [Fact]
    public void A_time_picker_a_checkbox_and_a_radio_group_say_aria_required_for_a_registered_field()
    {
        var trip = new Trip();

        var page = Page.Render(() => Div[
            Ui.TimePicker.Bind(() => trip.At).Locale("en-US").Label("At").Id("at"),
            Ui.Checkbox.Bind(() => trip.Agreed).Label("I agree").Id("agreed"),
            Ui.RadioGroup.Bind(() => trip.Country).Label("Country").Id("plan")[
                Ui.Radio.Value("hu").Label("Hungary"),
                Ui.Radio.Value("gb").Label("United Kingdom")
            ]
        ]);

        Assert.True(page.Exists("#at[aria-required=\"true\"]"));
        Assert.True(page.Exists("#agreed[aria-required=\"true\"]"));
        Assert.True(page.Exists("#plan[role=\"radiogroup\"][aria-required=\"true\"]"));
        Assert.Equal(3, page.FindAll("[aria-required]").Count);
    }

    [Fact]
    public void A_control_says_aria_required_for_a_member_that_carries_the_attribute()
    {
        var trip = new Trip();

        var page = Page.Render(() => Ui.Input.Bind(() => trip.Passport).Label("Passport").Id("passport"));

        Assert.True(page.Exists("input#passport[aria-required=\"true\"]"));
    }

    [Fact]
    public void No_control_says_aria_required_for_a_field_nobody_registered()
    {
        var trip = new Trip();

        var page = Page.Render(() => Div[
            Ui.Input.Bind(() => trip.Notes).Label("Notes"),
            Ui.Textarea.Bind(() => trip.Notes).Id("more"),
            Ui.Select.Bind(() => trip.Seat).Label("Seat")[Countries()],
            Ui.Select.Bind(() => trip.Seat).Variant(Ui.SelectVariant.Listbox).Id("seat")[Countries()],
            Ui.DatePicker.Bind(() => trip.Returns).Locale("en-US").On(new DateOnly(2026, 10, 9)).Label("Returns")
        ]);

        Assert.Empty(page.FindAll("[aria-required]"));
    }

    [Fact]
    public void A_slider_says_no_aria_required_because_its_role_has_none()
    {
        var trip = new Trip();

        var page = Page.Render(() => Ui.Slider.Bind(() => trip.Bags));

        Assert.Empty(page.FindAll("[aria-required]"));
    }

    private static Rask.Core.Component[] Countries() =>
    [
        Ui.SelectOption.Value("hu")["Hungary"],
        Ui.SelectOption.Value("gb")["United Kingdom"],
    ];

    // What a generator emits for a model: required is said by the type, with no attribute on the member.
    private sealed class Trip
    {
        public string Name { get; set; } = "Ada";
        public string Country { get; set; } = "hu";
        public DateOnly Leaves { get; set; } = new(2026, 10, 20);
        public TimeOnly At { get; set; } = new(9, 30);
        public bool Agreed { get; set; }
        public int Bags { get; set; } = 1;

        [Required] public string Passport { get; set; } = "";

        public string Notes { get; set; } = "";
        public string Seat { get; set; } = "hu";
        public DateOnly Returns { get; set; } = new(2026, 10, 27);

        internal static bool IsRequired(string property) =>
            property is nameof(Name) or nameof(Country) or nameof(Leaves) or nameof(At) or nameof(Agreed) or nameof(Bags);
    }
}
