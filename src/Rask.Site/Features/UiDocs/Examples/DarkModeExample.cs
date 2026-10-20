namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DarkModeExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.ButtonGroup[
                Ui.Button.Icon(Ui.IconName.Sun).Attributes(("onclick", "Rask.appearance = 'light'"))["Light"],
                Ui.Button.Icon(Ui.IconName.Moon).Attributes(("onclick", "Rask.appearance = 'dark'"))["Dark"],
                Ui.Button.Icon(Ui.IconName.ComputerDesktop).Attributes(("onclick", "Rask.appearance = 'system'"))["System"]
            ],
            Ui.Button.Subtle.Icon(Ui.IconName.Moon).Tooltip("Toggle dark mode").Attributes(("onclick", "Rask.dark = !Rask.dark"))
        ];
}
