using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// A model type tells a bound text control the most characters its field may hold (RaskValidation.RegisterFieldLengths),
// and the control says it as maxlength — the length a value object declares, told to the browser up front.
public partial class FieldLengthTests : global::Rask.Core.RaskMarkup
{
    static FieldLengthTests() =>
        RaskValidation.RegisterFieldLengths(typeof(Driver), static name => name == nameof(Driver.Name) ? 40 : null);

    [Fact]
    public void A_bound_input_says_the_length_its_models_type_registered()
    {
        var driver = new Driver();

        var html = Page.Render(() => Input.Bind(() => driver.Name).Id("name")).Render();

        Assert.Contains("maxlength=\"40\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_textarea_says_the_length_its_models_type_registered()
    {
        var driver = new Driver();

        var html = Page.Render(() => Textarea.Bind(() => driver.Name).Id("name")).Render();

        Assert.Contains("maxlength=\"40\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_length_the_author_set_is_kept()
    {
        var driver = new Driver();

        var html = Page.Render(() => Input.Bind(() => driver.Name).MaxLength(10)).Render();

        Assert.Contains("maxlength=\"10\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("maxlength=\"40\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_with_no_registered_length_and_an_input_that_takes_none_say_nothing()
    {
        var driver = new Driver();

        var unregistered = Page.Render(() => Input.Bind(() => driver.Note)).Render();
        var notText = Page.Render(() => Input.Bind(() => driver.Name).Type(InputType.Number)).Render();

        Assert.DoesNotContain("maxlength", unregistered, StringComparison.Ordinal);
        Assert.DoesNotContain("maxlength", notText, StringComparison.Ordinal);
    }

    private sealed class Driver
    {
        public string Name { get; set; } = "";

        public string Note { get; set; } = "";
    }
}
