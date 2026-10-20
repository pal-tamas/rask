namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ButtonIconsExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-3")[
            Ui.Button.Icon(Ui.IconName.EllipsisHorizontal).AriaLabel("More"),
            Ui.Button.Icon(Ui.IconName.ArrowDownTray)["Export"],
            Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Open"],
            Ui.Button.Subtle.Icon(Ui.IconName.XMark).AriaLabel("Close"),
            Ui.Button.Icon(Ui.IconName.Plus).Tooltip("Add").TooltipKbd("N")
        ];
}
