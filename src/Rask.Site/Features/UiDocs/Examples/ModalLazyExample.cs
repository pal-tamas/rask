namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ModalLazyExample : Component
{
    protected override Component? Render() =>
        Div[
            Ui.ModalTrigger.Name("lazy-profile")[Ui.Button["Edit profile"]],
            Ui.Modal.Name("lazy-profile").Lazy().Class("md:w-96")[
                Div.Class("space-y-6")[
                    Div[
                        Ui.Heading.Level(2).Lg["Update profile"],
                        Ui.Text.Class("mt-2")["None of this reached the browser until you opened it."]
                    ],
                    Ui.Input.Of<string>().Label("Name").Placeholder("Your name"),
                    Div.Class("flex")[Ui.Spacer, Ui.ModalClose[Ui.Button.Primary["Done"]]]
                ]
            ]
        ];
}
