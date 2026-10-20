namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ModalFlyoutExample : Component
{
    protected override Component? Render() =>
        Div[
            Ui.ModalTrigger.Name("edit-profile-flyout")[Ui.Button["Edit profile"]],
            Ui.Modal.Name("edit-profile-flyout").Flyout().Class("md:w-lg")[
                Div.Class("space-y-6")[
                    Div[
                        Ui.Heading.Level(2).Lg["Update profile"],
                        Ui.Text.Class("mt-2")["Make changes to your personal details."]
                    ],
                    Ui.Input.Of<string>().Label("Name").Placeholder("Your name"),
                    Ui.Input.Of<string>().Label("Date of birth").Type(InputType.Date),
                    Div.Class("flex")[Ui.Spacer, Ui.Button.Primary["Save changes"]]
                ]
            ]
        ];
}
