namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TooltipExample : Component
{
    // An icon button has no text of its own, so the tooltip becomes its name.
    protected override Component? Render() =>
        Div.Class("flex pt-8")[
            Ui.Tooltip.Content("Settings")[
                Ui.Button.Icon(Ui.IconName.Cog6Tooth).IconVariant(Ui.IconVariant.Outline)
            ]
        ];
}
