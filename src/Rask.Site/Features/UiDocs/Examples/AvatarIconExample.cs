namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AvatarIconExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-6")[
            Ui.Avatar.Key("user").Icon(Ui.IconName.User),
            Ui.Avatar.Key("phone").Icon(Ui.IconName.Phone),
            Ui.Avatar.Key("desktop").Icon(Ui.IconName.ComputerDesktop)
        ];
}
