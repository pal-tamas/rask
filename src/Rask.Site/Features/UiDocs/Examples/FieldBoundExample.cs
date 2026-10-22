namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FieldBoundExample : Component
{
    private readonly Signup _signup = new();

    protected override Component? Render() =>
        Div.Class("space-y-4")[
            Form.Model(_signup)[
                Div.Class("grid gap-4 sm:grid-cols-2")[
                    Ui.Input.Bind(() => _signup.Email).Live().Label("Email").Type(InputType.Email),
                    // T is the model's, so this is a number field with nothing said here.
                    Ui.Input.Bind(() => _signup.Seats).Live().Label("Seats")
                ],
                Div.Class("mt-4")[Ui.Checkbox.Bind(() => _signup.Agreed).Live().Label("I agree to the terms")]
            ],
            Ui.Text.Data("testid", "ui-bound-state")[
                $"{_signup.Email} · {_signup.Seats.ToString(System.Globalization.CultureInfo.InvariantCulture)} seats"
                + $" · agreed {(_signup.Agreed ? "yes" : "no")}"
            ]
        ];

    // An ordinary model: nothing on it knows it is being edited by a UI kit.
    public sealed class Signup
    {
        public string Email { get; set; } = "";

        public int Seats { get; set; } = 1;

        public bool Agreed { get; set; }
    }
}
