namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ModalFlyoutPositionExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-2")[
            Ui.ModalTrigger.Name("flyout-left")[Ui.Button["From the left"]],
            Ui.ModalTrigger.Name("flyout-bottom")[Ui.Button["From the bottom"]],
            Ui.Modal.Name("flyout-left").Flyout().Left[
                Div.Class("space-y-6")[Ui.Heading.Level(2).Lg["Navigation"], Ui.Text["A flyout from the left edge."]]
            ],
            Ui.Modal.Name("flyout-bottom").Flyout().Bottom[
                Div.Class("space-y-6")[Ui.Heading.Level(2).Lg["Share"], Ui.Text["A flyout from the bottom edge."]]
            ]
        ];
}
