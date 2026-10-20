namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SeparatorVerticalExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex items-center justify-center gap-6")[
            Ui.Button.Ghost.Icon(Ui.IconName.Moon).AriaLabel("Switch to dark theme"),
            Ui.Separator.Vertical(),
            Ui.Button["Log in"]
        ];
}
