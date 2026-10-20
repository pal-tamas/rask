namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ModalDismissibleExample : Component
{
    protected override Component? Render() =>
        Div[
            Ui.ModalTrigger.Name("demo-filters")[Ui.Button["Filters"]],
            Ui.Modal.Name("demo-filters").Flyout().Dismissible(false)[
                Div.Class("space-y-6")[
                    Ui.Heading.Level(2).Lg["Filters"],
                    Ui.Text["Only the close button, Escape, or Apply closes this one."],
                    Div.Class("flex")[Ui.Spacer, Ui.ModalClose[Ui.Button.Primary["Apply"]]]
                ]
            ]
        ];
}
