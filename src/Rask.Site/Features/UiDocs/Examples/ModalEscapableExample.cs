namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ModalEscapableExample : Component
{
    protected override Component? Render() =>
        Div[
            Ui.ModalTrigger.Name("demo-session")[Ui.Button["Session"]],
            Ui.Modal.Name("demo-session").Escapable(false)[
                Div.Class("space-y-6")[
                    Ui.Heading.Level(2).Lg["Session expiring"],
                    Ui.Text["Escape does not close this one; a click outside, the corner button or Stay does."],
                    Div.Class("flex")[Ui.Spacer, Ui.ModalClose[Ui.Button.Primary["Stay signed in"]]]
                ]
            ]
        ];
}
