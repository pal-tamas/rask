namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TooltipDisabledExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex pt-8")[
            Ui.Tooltip.Content("Available once the form is valid")[
                Ui.Button.Sm.Disabled()["Disabled"]
            ]
        ];
}
