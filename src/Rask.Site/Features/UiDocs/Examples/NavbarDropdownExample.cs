namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class NavbarDropdownExample : Component
{
    protected override Component? Render() =>
        Ui.Navbar[
            Ui.NavbarItem.Key("dashboard").Href("#").Current(true).Accent(false)["Dashboard"],
            Ui.NavbarItem.Key("transactions").Href("#")["Transactions"],
            Ui.Dropdown.Key("account")[
                Ui.NavbarItem.IconTrailing(Ui.IconName.ChevronDown)["Account"],
                Ui.Navmenu[
                    Ui.NavmenuItem.Href("#")["Profile"],
                    Ui.NavmenuItem.Href("#")["Settings"],
                    Ui.NavmenuItem.Href("#")["Billing"]
                ]
            ]
        ];
}
