namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarBadgeExample : Component
{
    private const string Caleb = "/img/avatars/amber.svg";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-6")[
            Ui.Avatar.Key("dot").Badge("").BadgeColor(Ui.Color.Green).Src(Caleb).Name("Caleb Porzio"),
            Ui.Avatar.Key("outline").Badge("").BadgeColor(Ui.Color.Zinc).BadgePosition(Ui.AvatarBadgePosition.TopRight)
                .BadgeCircle().BadgeVariant(Ui.AvatarBadgeVariant.Outline).Src(Caleb).Name("Caleb Porzio"),
            Ui.Avatar.Key("count").Badge("25").Src(Caleb).Name("Caleb Porzio"),
            Ui.Avatar.Key("emoji").Circle().Badge("👍").BadgeCircle().Src(Caleb).Name("Caleb Porzio"),
            Ui.Avatar.Key("slot").Circle()
                .Badge(Img.Src("/img/avatars/sky.svg").Alt("").Class("size-3"))
                .Src(Caleb).Name("Caleb Porzio")
        ];
}
