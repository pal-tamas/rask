namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TooltipKbdExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex pt-8")[
            Ui.Tooltip.Content("Save").Kbd("⌘S")[Ui.Button.Sm["Shortcut"]]
        ];
}
