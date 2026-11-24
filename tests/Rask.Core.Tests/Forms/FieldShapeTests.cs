using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// A model type tells a bound control what its field is (RaskValidation.RegisterFieldShapes), and a text control says
// the lengths as maxlength and minlength — what a value object declares, told to the browser up front.
public partial class FieldShapeTests : global::Rask.Core.RaskMarkup
{
    static FieldShapeTests()
    {
        RaskValidation.RegisterFieldShapes(typeof(Driver), static name => name == nameof(Driver.Name)
            ? new FieldShape(FieldKind.Text, MaxLength: 40, MinLength: 2)
            : null);
        RaskValidation.RegisterFieldShapes(typeof(Leg), static name => name == nameof(Leg.Kind)
            ? new FieldShape(FieldKind.Choice, Options: [new FieldOption(LegKind.Road, "On the road")])
            : null);
    }

    [Fact]
    public void A_bound_input_says_the_lengths_its_models_type_registered()
    {
        var driver = new Driver();

        var html = Page.Render(() => Input.Bind(() => driver.Name).Id("name")).Render();

        Assert.Contains("maxlength=\"40\"", html, StringComparison.Ordinal);
        Assert.Contains("minlength=\"2\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_textarea_says_the_lengths_its_models_type_registered()
    {
        var driver = new Driver();

        var html = Page.Render(() => Textarea.Bind(() => driver.Name).Id("name")).Render();

        Assert.Contains("maxlength=\"40\"", html, StringComparison.Ordinal);
        Assert.Contains("minlength=\"2\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_length_the_author_set_is_kept()
    {
        var driver = new Driver();

        var html = Page.Render(() => Input.Bind(() => driver.Name).MaxLength(10).MinLength(1)).Render();

        Assert.Contains("maxlength=\"10\"", html, StringComparison.Ordinal);
        Assert.Contains("minlength=\"1\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("maxlength=\"40\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("minlength=\"2\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_with_no_registered_shape_and_an_input_that_takes_no_length_say_nothing()
    {
        var driver = new Driver();

        var unregistered = Page.Render(() => Input.Bind(() => driver.Note)).Render();
        var notText = Page.Render(() => Input.Bind(() => driver.Name).Type(InputType.Number)).Render();

        Assert.DoesNotContain("maxlength", unregistered, StringComparison.Ordinal);
        Assert.DoesNotContain("minlength", unregistered, StringComparison.Ordinal);
        Assert.DoesNotContain("maxlength", notText, StringComparison.Ordinal);
        Assert.DoesNotContain("minlength", notText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shape_is_found_for_a_type_derived_from_the_registered_one_and_kept()
    {
        var derived = new LongLeg();
        var bound = ExpressionAccessor.Parse(() => derived.Kind);

        var first = RaskValidation.ShapeOf(bound);
        var again = RaskValidation.ShapeOf(bound);

        Assert.Equal(FieldKind.Choice, first!.Kind);
        Assert.Equal([new FieldOption(LegKind.Road, "On the road")], first.Options!);
        Assert.Same(first, again);
    }

    [Fact]
    public void A_hand_written_models_attributes_tell_its_input_the_lengths_the_type_and_that_it_is_not_edited()
    {
        var contact = new Contact();

        var page = Page.Render(() => Div[
            Input.Bind(() => contact.Email).Id("email"),
            Input.Bind(() => contact.Phone).Id("phone"),
            Input.Bind(() => contact.Code).Id("code"),
            Textarea.Bind(() => contact.Code).Id("code-text")
        ]);

        var email = page.Find("#email");
        Assert.Equal(("email", "80", "5"), (email.Attribute("type"), email.Attribute("maxlength"), email.Attribute("minlength")));
        Assert.Equal("tel", page.Find("#phone").Attribute("type"));
        Assert.Equal(("text", "12"), (page.Find("#code").Attribute("type"), page.Find("#code").Attribute("maxlength")));
        Assert.True(page.Exists("#code[readonly]"));
        Assert.True(page.Exists("#code-text[readonly]"));
    }

    [Fact]
    public void The_tightest_of_several_length_attributes_is_the_one_said()
    {
        var contact = new Contact();

        var html = Page.Render(() => Input.Bind(() => contact.Name)).Render();

        Assert.Contains("maxlength=\"30\"", html, StringComparison.Ordinal);
        Assert.Contains("minlength=\"3\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_date_typed_moment_is_a_date_input_showing_its_day_and_reads_the_day_back()
    {
        var contact = new Contact { Born = new DateTime(1990, 5, 17, 13, 45, 0, DateTimeKind.Unspecified) };

        var page = Page.Render(() => Form.Model(contact)[Input.Bind(() => contact.Born)]);
        var input = page.Find("input");

        Assert.Equal(("date", "1990-05-17"), (input.Attribute("type"), input.Attribute("value")));
    }

    [Fact]
    public async Task An_emptied_text_kept_empty_by_its_display_format_is_written_as_empty_not_null()
    {
        var contact = new Contact { Note = "x", Remark = "y" };

        var page = Page.Render(() => Form.Model(contact)[Input.Bind(() => contact.Note).Id("note"), Input.Bind(() => contact.Remark).Id("remark")]);
        await page.On("#note").Change("");
        await page.On("#remark").Change("");

        Assert.Equal("", contact.Note);
        Assert.Null(contact.Remark);
    }

    [Fact]
    public void What_a_models_type_registered_wins_over_the_members_attributes()
    {
        var driver = new Driver();

        var html = Page.Render(() => Input.Bind(() => driver.Name)).Render();

        Assert.Contains("maxlength=\"40\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("readonly", html, StringComparison.Ordinal);
    }

    private sealed class Contact
    {
        [EmailAddress, StringLength(80, MinimumLength = 5)]
        public string Email { get; set; } = "";

        [Phone]
        public string Phone { get; set; } = "";

        [Editable(false), MaxLength(12)]
        public string Code { get; set; } = "";

        [StringLength(40, MinimumLength = 2), MaxLength(30), MinLength(3)]
        public string Name { get; set; } = "";

        [DataType(DataType.Date)]
        public DateTime Born { get; set; }

        [DisplayFormat(ConvertEmptyStringToNull = false)]
        public string? Note { get; set; }

        public string? Remark { get; set; }
    }

    private sealed class Driver
    {
        [Editable(false)]
        public string Name { get; set; } = "";

        public string Note { get; set; } = "";
    }

    private enum LegKind
    {
        Road,
    }

    private class Leg
    {
        public LegKind Kind { get; set; }
    }

    private sealed class LongLeg : Leg;
}
