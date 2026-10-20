namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TooltipAlignExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex pb-8")[
            Ui.Tooltip.Content("Flush with its start edge").Bottom.Start[Ui.Button.Sm["Aligned"]]
        ];
}
