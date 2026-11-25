using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// What a field's shape tells the input bound to it beyond its lengths: a kind of text (Email, Phone, Password) its type,
// autocomplete and keyboard; a Number or an Integer its bounds and its step; a day and a moment their input type.
public partial class ShapedInputAttributesTests : global::Rask.Core.RaskMarkup
{
    static ShapedInputAttributesTests()
    {
        RaskValidation.RegisterFieldShapes(typeof(Signup), static name => name switch
        {
            nameof(Signup.Email) => new FieldShape(FieldKind.Text, MaxLength: 254) { Input = InputType.Email, AutoComplete = "email" },
            nameof(Signup.Phone) => new FieldShape(FieldKind.Text, MaxLength: 32)
            {
                Input = InputType.Tel,
                AutoComplete = "tel",
                InputMode = Core.InputMode.Tel,
            },
            nameof(Signup.Password) => new FieldShape(FieldKind.Text, MinLength: 8) { Input = InputType.Password, AutoComplete = "new-password" },
            nameof(Signup.Litres) => new FieldShape(FieldKind.Number, Min: 1, Max: 999.5m, Scale: 2),
            nameof(Signup.Pieces) => new FieldShape(FieldKind.Integer, Min: 0, Max: 1000),
            nameof(Signup.Invoiced) => new FieldShape(FieldKind.Day, NotAfterToday: true),
            nameof(Signup.Fuelled) => new FieldShape(FieldKind.Moment, NotAfterNow: true),
            _ => null,
        });
    }

    [Fact]
    public void An_email_field_is_an_email_input_the_browser_fills_with_an_address()
    {
        var signup = new Signup();

        var html = Page.Render(() => Input.Bind(() => signup.Email)).Render();

        Assert.Contains("autocomplete=\"email\"", html, StringComparison.Ordinal);
        Assert.Contains("type=\"email\"", html, StringComparison.Ordinal);
        Assert.Contains("maxlength=\"254\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_phone_field_is_a_tel_input_with_the_phone_keyboard()
    {
        var signup = new Signup();

        var html = Page.Render(() => Input.Bind(() => signup.Phone)).Render();

        Assert.Contains("type=\"tel\"", html, StringComparison.Ordinal);
        Assert.Contains("inputmode=\"tel\"", html, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"tel\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_password_field_is_a_password_input_that_never_writes_what_it_holds()
    {
        var signup = new Signup { Password = "hunter22" };

        var html = Page.Render(() => Input.Bind(() => signup.Password)).Render();

        Assert.Contains("type=\"password\"", html, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"new-password\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter22", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_autocomplete_and_a_keyboard_the_author_set_are_kept()
    {
        var signup = new Signup();

        var html = Page.Render(() => Input.Bind(() => signup.Phone).Autocomplete("off").InputMode(Core.InputMode.Numeric)).Render();

        Assert.Contains("autocomplete=\"off\"", html, StringComparison.Ordinal);
        Assert.Contains("inputmode=\"numeric\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("autocomplete=\"tel\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_number_field_is_given_its_bounds_and_the_step_its_scale_calls_for()
    {
        var signup = new Signup();

        var html = Page.Render(() => Input.Bind(() => signup.Litres)).Render();

        Assert.Contains("type=\"number\"", html, StringComparison.Ordinal);
        Assert.Contains("min=\"1\"", html, StringComparison.Ordinal);
        Assert.Contains("max=\"999.5\"", html, StringComparison.Ordinal);
        Assert.Contains("step=\"0.01\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_whole_number_field_steps_by_one_within_its_bounds()
    {
        var signup = new Signup();

        var html = Page.Render(() => Input.Bind(() => signup.Pieces)).Render();

        Assert.Contains("min=\"0\"", html, StringComparison.Ordinal);
        Assert.Contains("max=\"1000\"", html, StringComparison.Ordinal);
        Assert.Contains("step=\"1\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Bounds_the_author_set_are_kept()
    {
        var signup = new Signup();

        var html = Page.Render(() => Input.Bind(() => signup.Litres).Min("5").Step("0.5")).Render();

        Assert.Contains("min=\"5\"", html, StringComparison.Ordinal);
        Assert.Contains("step=\"0.5\"", html, StringComparison.Ordinal);
        Assert.Contains("max=\"999.5\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_day_is_a_date_input_and_a_moment_a_date_and_time_one()
    {
        var signup = new Signup();

        var day = Page.Render(() => Input.Bind(() => signup.Invoiced)).Render();
        var moment = Page.Render(() => Input.Bind(() => signup.Fuelled)).Render();

        Assert.Contains("type=\"date\"", day, StringComparison.Ordinal);
        Assert.Contains("type=\"datetime-local\"", moment, StringComparison.Ordinal);
    }

    private sealed class Signup
    {
        public string? Email { get; set; }

        public string? Phone { get; set; }

        public string? Password { get; set; }

        public decimal? Litres { get; set; }

        public long? Pieces { get; set; }

        public DateOnly? Invoiced { get; set; }

        public DateTime? Fuelled { get; set; }
    }
}
