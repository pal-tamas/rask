namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FieldTrailingDescriptionExample : Component
{
    private const string Rules =
        "Must be at least 8 characters long, include an uppercase letter, a number, and a special character.";

    protected override Component? Render() =>
        Div.Class("grid max-w-sm gap-8")[
            Ui.Field[
                Ui.Label["Password"],
                Ui.Input.Of<string>().Type(InputType.Password),
                Ui.Error,
                Ui.Description[Rules]
            ],
            // The same field, from the input's own steps.
            Ui.Input.Of<string>().Type(InputType.Password).Label("Password").DescriptionTrailing(Rules)
        ];
}
