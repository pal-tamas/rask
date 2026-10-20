namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TooltipPositionExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-8 px-12 py-8")[
            Ui.Tooltip.Key("top").Content("Settings").Top[Settings()],
            Ui.Tooltip.Key("right").Content("Settings").Right[Settings()],
            Ui.Tooltip.Key("bottom").Content("Settings").Bottom[Settings()],
            Ui.Tooltip.Key("left").Content("Settings").Left[Settings()]
        ];

    private static UiButton Settings() =>
        Ui.Button.Icon(Ui.IconName.Cog6Tooth).IconVariant(Ui.IconVariant.Outline);
}
