using Rask.Values;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FieldKindsExample : Component
{
    private readonly Signup _signup = new();

    protected override Component? Render() =>
        Div.Class("space-y-4")[
            Form.Model(_signup)[
                Div.Class("grid gap-4 sm:grid-cols-2")[
                    // Each control is its value object's kind: an email input that autocompletes an address, a tel
                    // input with the phone keyboard, a url input, a password input for a new password, a textarea.
                    Ui.Field.Bind(() => _signup.Email).Label("Email").Live(),
                    Ui.Field.Bind(() => _signup.Phone).Label("Phone").Live(),
                    Ui.Field.Bind(() => _signup.Site).Label("Website").Live(),
                    Ui.Field.Bind(() => _signup.Password).Label("Choose a password").Live()
                ],
                Div.Class("mt-4")[Ui.Field.Bind(() => _signup.Remark).Label("Anything else?").Live()]
            ],
            Ui.Text.Data("testid", "ui-field-kinds-state")[
                $"{_signup.Email} · {_signup.Phone} · {_signup.Site} · password {(string.IsNullOrEmpty(_signup.Password) ? "empty" : "typed")}"
            ]
        ];

    // The value objects: each kind of text carries its rule and tells its input what it is — the same as
    // [EmailAddress], [Phone], [Url], [DataType(DataType.Password)] and [DataType(DataType.MultilineText)] would.
    public sealed partial record ContactEmail(string Value) : Email(Value, MaxLength: 120);

    public sealed partial record ContactPhone(string Value) : Phone(Value, Required: false);

    public sealed partial record Homepage(string Value) : Url(Value, Required: false);

    public sealed partial record ChosenPassword(string Value) : Password(Value, MinLength: 10, New: true);

    public sealed partial record Remark(string Value) : Multiline(Value, MaxLength: 500, Required: false);

    // The form's model. An aggregate's generated model (Rask.Data) is one of these with each field's value object
    // registered for it; this site has no data layer, so FieldKindsExample.Model.cs registers the same.
    public sealed class Signup
    {
        public string? Email { get; set; }

        public string? Phone { get; set; }

        public string? Site { get; set; }

        public string? Password { get; set; }

        public string? Remark { get; set; }
    }
}
