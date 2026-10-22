namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class InputTypesExample : Component
{
    protected override Component? Render() =>
        Div.Class("grid max-w-sm gap-6")[
            Ui.Input.Of<string>().Type(InputType.Email).Label("Email"),
            Ui.Input.Of<string>().Type(InputType.Password).Label("Password"),
            Ui.Input.Of<string>().Type(InputType.Date).Max("2999-12-31").Label("Date")
        ];
}
