namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ProfileMenuExample : Component
{
    protected override Component? Render() =>
        Ui.Dropdown.Bottom.Start[
            Ui.Profile.Name("Olivia Martin").Avatar("/img/avatars/rose.svg"),
            Ui.Navmenu[
                Ui.NavmenuItem.Href("#").Icon(Ui.IconName.User)["Account"],
                Ui.NavmenuItem.Href("#").Icon(Ui.IconName.Cog6Tooth)["Settings"],
                Ui.NavmenuItem.Href("#").Icon(Ui.IconName.ArrowRightStartOnRectangle)["Logout"]
            ]
        ];
}
