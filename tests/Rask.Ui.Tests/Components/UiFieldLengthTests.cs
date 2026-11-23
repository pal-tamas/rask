using Rask.Core.Forms;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.Input</c> and <c>Ui.Textarea</c> bound to a field whose model registered its shape say its lengths as
///     <c>maxlength</c> and <c>minlength</c>, and a length the author set is kept.
/// </summary>
public partial class UiFieldLengthTests : global::Rask.Core.RaskMarkup
{
    static UiFieldLengthTests() =>
        RaskValidation.RegisterFieldShapes(typeof(Driver), static name => name == nameof(Driver.Name)
            ? new FieldShape(FieldKind.Text, MaxLength: 40, MinLength: 3)
            : null);

    [Fact]
    public void A_bound_input_says_the_fewest_and_the_most_characters_its_field_takes()
    {
        var driver = new Driver();

        var html = Ui.Input.Bind(() => driver.Name).Label("Name").ToHtml();

        Assert.Contains("minlength=\"3\"", html, StringComparison.Ordinal);
        Assert.Contains("maxlength=\"40\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_textarea_says_the_fewest_and_the_most_characters_its_field_takes()
    {
        var driver = new Driver();

        var html = Ui.Textarea.Bind(() => driver.Name).Label("Name").ToHtml();

        Assert.Contains("minlength=\"3\"", html, StringComparison.Ordinal);
        Assert.Contains("maxlength=\"40\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fewest_the_author_set_is_kept_on_both()
    {
        var driver = new Driver();

        var input = Ui.Input.Bind(() => driver.Name).MinLength(5).ToHtml();
        var textarea = Ui.Textarea.Bind(() => driver.Name).MinLength(5).ToHtml();

        Assert.Contains("minlength=\"5\"", input, StringComparison.Ordinal);
        Assert.Contains("minlength=\"5\"", textarea, StringComparison.Ordinal);
        Assert.DoesNotContain("minlength=\"3\"", input, StringComparison.Ordinal);
        Assert.DoesNotContain("minlength=\"3\"", textarea, StringComparison.Ordinal);
    }

    private sealed class Driver
    {
        public string Name { get; set; } = "";
    }
}
