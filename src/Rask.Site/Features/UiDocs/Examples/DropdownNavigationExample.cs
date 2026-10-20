namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DropdownNavigationExample : Component
{
    protected override Component? Render() =>
        Ui.Dropdown.Bottom.End[
            Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Olivia Martin"],
            Ui.Navmenu[
                Ui.NavmenuItem.Href("#account").Icon(Ui.IconName.User)["Account"],
                Ui.NavmenuItem.Href("#profile").Icon(Ui.IconName.BuildingStorefront)["Profile"],
                Ui.NavmenuItem.Href("#billing").Icon(Ui.IconName.CreditCard)["Billing"],
                Ui.NavmenuItem.Href("#logout").Icon(Ui.IconName.ArrowRightStartOnRectangle)["Logout"],
                Ui.NavmenuItem.Href("#delete").Icon(Ui.IconName.Trash).Danger["Delete"]
            ]
        ];
}
