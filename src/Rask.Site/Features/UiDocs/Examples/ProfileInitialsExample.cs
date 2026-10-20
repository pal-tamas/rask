namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ProfileInitialsExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-6")[
            Ui.Profile.Key("initials").Name("Caleb Porzio"),
            Ui.Profile.Key("colour").Name("Caleb Porzio").AvatarColor(Ui.Color.Cyan),
            Ui.Profile.Key("stated").Initials("CP"),
            Ui.Profile.Key("avatar-name").AvatarName("Caleb Porzio")
        ];
}
