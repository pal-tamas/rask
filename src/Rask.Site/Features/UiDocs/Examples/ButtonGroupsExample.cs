namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ButtonGroupsExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-3")[
            Ui.ButtonGroup[
                Ui.Button["Oldest"],
                Ui.Button["Newest"],
                Ui.Button["Top"]
            ],
            Ui.ButtonGroup[
                Ui.Button.Icon(Ui.IconName.Bars3BottomLeft).Tooltip("Align left"),
                Ui.Button.Icon(Ui.IconName.Bars3).Tooltip("Justify"),
                Ui.Button.Icon(Ui.IconName.Bars3BottomRight).Tooltip("Align right")
            ],
            Ui.ButtonGroup[
                Ui.Button["New product"],
                Ui.Button.Icon(Ui.IconName.ChevronDown).AriaLabel("More options")
            ]
        ];
}
