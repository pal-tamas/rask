namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FieldBadgeExample : Component
{
    protected override Component? Render() =>
        Div.Class("grid max-w-sm gap-8")[
            Ui.Field[
                Ui.Label.Badge("Required")["Email"],
                Ui.Input.Of<string>().Type(InputType.Email).Attributes(("required", "")),
                Ui.Error
            ],
            Ui.Field[
                Ui.Label.Badge("Optional")["Phone number"],
                Ui.Input.Of<string>().Type(InputType.Tel).Placeholder("(555) 555-5555").Mask("(999) 999-9999"),
                Ui.Error
            ]
        ];
}
