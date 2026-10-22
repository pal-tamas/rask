namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FieldExample : Component
{
    private string _email = "";

    protected override Component? Render()
    {
        var bad = _email.Length > 0 && !_email.Contains('@');

        return Ui.Field.Class("max-w-sm")[
            Ui.Label["Email"],
            Ui.Input.Value(_email).Type(InputType.Email).Invalid(bad).OnChange(email => { _email = email; }),
            Ui.Error.Message(bad ? "That does not look like an email address." : null)
        ];
    }
}
