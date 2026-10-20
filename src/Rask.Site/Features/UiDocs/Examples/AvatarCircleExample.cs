namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarCircleExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-6")[
            Ui.Avatar.Key("image").Circle().Src("/img/avatars/amber.svg").Name("Caleb Porzio"),
            Ui.Avatar.Key("initials").Circle().Name("Caleb Porzio")
        ];
}
