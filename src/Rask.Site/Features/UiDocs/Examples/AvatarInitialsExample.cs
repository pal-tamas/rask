namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarInitialsExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-6")[
            Ui.Avatar.Key("two").Name("Caleb Porzio"),
            Ui.Avatar.Key("word").Name("calebporzio"),
            Ui.Avatar.Key("single").Name("calebporzio").InitialsSingle(),
            Ui.Avatar.Key("stated").Initials("CP")
        ];
}
