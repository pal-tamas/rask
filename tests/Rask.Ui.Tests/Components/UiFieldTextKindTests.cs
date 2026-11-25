using Rask.Core.Forms;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     A field whose value object is a kind of text — Email, Phone, Password, Multiline — draws the control the matching
///     attribute would, through <c>Ui.Field.Bind</c> and <c>Ui.Input.Bind</c> alike; and a bare <c>Ui.Input.Bind</c> over a
///     number, a day or a moment is told what <c>Ui.Field.Bind</c> is.
/// </summary>
public partial class UiFieldTextKindTests : global::Rask.Core.RaskMarkup
{
    // What the generated __Field of each kind says (TextKindGeneratorTests proves the generator writes these).
    static UiFieldTextKindTests() =>
        RaskValidation.RegisterFieldShapes(typeof(Account), static name => name switch
        {
            nameof(Account.Email) => new FieldShape(FieldKind.Text, MaxLength: 200) { Input = global::Rask.Core.InputType.Email, AutoComplete = "email" },
            nameof(Account.Phone) => new FieldShape(FieldKind.Text, MaxLength: 32)
            {
                Input = global::Rask.Core.InputType.Tel,
                AutoComplete = "tel",
                InputMode = global::Rask.Core.InputMode.Tel,
            },
            nameof(Account.Password) => new FieldShape(FieldKind.Text, MinLength: 8) { Input = global::Rask.Core.InputType.Password, AutoComplete = "current-password" },
            nameof(Account.Remark) => new FieldShape(FieldKind.Text, MaxLength: 4000) { Hint = FieldHints.MultilineText },
            nameof(Account.Litres) => new FieldShape(FieldKind.Number, Min: 1, Max: 500, Scale: 2),
            nameof(Account.Pieces) => new FieldShape(FieldKind.Integer, Min: 2, Max: 9),
            nameof(Account.Invoiced) => new FieldShape(FieldKind.Day),
            nameof(Account.Fuelled) => new FieldShape(FieldKind.Moment),
            _ => null,
        });

    [Fact]
    public void An_email_value_object_is_an_email_input_with_its_autocomplete_in_a_field_and_bare()
    {
        var account = new Account();

        var field = Page.Render(() => Ui.Field.Bind(() => account.Email)).Find("input#f-email");
        var bare = Page.Render(() => Ui.Input.Bind(() => account.Email).Id("e")).Find("input#e");

        Assert.Equal(("email", "email", "200"), (field.Attribute("type"), field.Attribute("autocomplete"), field.Attribute("maxlength")));
        Assert.Equal(("email", "email"), (bare.Attribute("type"), bare.Attribute("autocomplete")));
    }

    [Fact]
    public void A_phone_value_object_is_a_tel_input_with_the_phone_keyboard()
    {
        var account = new Account();

        var input = Page.Render(() => Ui.Field.Bind(() => account.Phone)).Find("input#f-phone");

        Assert.Equal(("tel", "tel", "tel"), (input.Attribute("type"), input.Attribute("inputmode"), input.Attribute("autocomplete")));
    }

    [Fact]
    public void A_password_value_object_is_a_password_input_that_never_shows_what_it_holds()
    {
        var account = new Account { Password = "hunter22" };

        var page = Page.Render(() => Ui.Field.Bind(() => account.Password));

        var input = page.Find("input#f-password");
        Assert.Equal(("password", "current-password"), (input.Attribute("type"), input.Attribute("autocomplete")));
        Assert.DoesNotContain("hunter22", page.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_multiline_value_object_is_a_textarea_unless_the_step_says_otherwise()
    {
        var account = new Account();

        var drawn = Page.Render(() => Ui.Field.Bind(() => account.Remark));
        var single = Page.Render(() => Ui.Field.Bind(() => account.Remark).Multiline(false));

        Assert.Equal("4000", drawn.Find("textarea#f-remark").Attribute("maxlength"));
        Assert.False(drawn.Exists("input#f-remark"));
        Assert.True(single.Exists("input#f-remark"));
    }

    [Fact]
    public void A_bare_input_over_a_number_takes_its_bounds_and_the_step_of_its_scale()
    {
        var account = new Account();

        var page = Page.Render(() => Div[Ui.Input.Bind(() => account.Litres).Id("l"), Ui.Input.Bind(() => account.Pieces).Id("p")]);

        var litres = page.Find("input#l");
        Assert.Equal(("number", "1", "500", "0.01"), (litres.Attribute("type"), litres.Attribute("min"), litres.Attribute("max"), litres.Attribute("step")));
        var pieces = page.Find("input#p");
        Assert.Equal(("2", "9", "1"), (pieces.Attribute("min"), pieces.Attribute("max"), pieces.Attribute("step")));
    }

    [Fact]
    public void A_bare_input_over_a_day_is_a_date_input_and_over_a_moment_a_date_and_time_one()
    {
        var account = new Account();

        var page = Page.Render(() => Div[Ui.Input.Bind(() => account.Invoiced).Id("d"), Ui.Input.Bind(() => account.Fuelled).Id("m")]);

        Assert.Equal("date", page.Find("input#d").Attribute("type"));
        Assert.Equal("datetime-local", page.Find("input#m").Attribute("type"));
    }

    private sealed class Account
    {
        public string? Email { get; set; }

        public string? Phone { get; set; }

        public string? Password { get; set; }

        public string? Remark { get; set; }

        public decimal? Litres { get; set; }

        public long? Pieces { get; set; }

        public DateOnly? Invoiced { get; set; }

        public DateTime? Fuelled { get; set; }
    }
}
