namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarInitialsExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-6")[
            Ui.Avatar.Key("two").Name("Mara Lind"),
            Ui.Avatar.Key("word").Name("maralind"),
            Ui.Avatar.Key("single").Name("maralind").InitialsSingle(),
            Ui.Avatar.Key("stated").Initials("ML")
        ];
}
