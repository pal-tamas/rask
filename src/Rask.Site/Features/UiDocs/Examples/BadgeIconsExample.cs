namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BadgeIconsExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-2")[
            Ui.Badge.Icon(Ui.IconName.UserCircle)["Users"],
            Ui.Badge.Icon(Ui.IconName.DocumentText)["Files"],
            Ui.Badge.IconTrailing(Ui.IconName.VideoCamera)["Videos"]
        ];
}
