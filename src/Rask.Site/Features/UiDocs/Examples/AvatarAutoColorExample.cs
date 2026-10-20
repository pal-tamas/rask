namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarAutoColorExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-2")[
            Ui.Avatar.Key("cp").Name("Caleb Porzio").ColorAuto(),
            Ui.Avatar.Key("mj").Name("Mia Jones").ColorAuto(),
            Ui.Avatar.Key("kc").Name("Kai Chen").ColorAuto(),
            Ui.Avatar.Key("seed").Name("Caleb Porzio").ColorAuto().ColorSeed("user-42")
        ];
}
