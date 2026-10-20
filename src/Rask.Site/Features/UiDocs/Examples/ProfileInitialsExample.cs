namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ProfileInitialsExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-6")[
            Ui.Profile.Key("initials").Name("Mara Lind"),
            Ui.Profile.Key("colour").Name("Mara Lind").AvatarColor(Ui.Color.Cyan),
            Ui.Profile.Key("stated").Initials("ML"),
            Ui.Profile.Key("avatar-name").AvatarName("Mara Lind")
        ];
}
