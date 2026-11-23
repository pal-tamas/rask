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

    private sealed class Driver
    {
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
