namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DropdownPositionExample : Component
{
    protected override Component? Render() =>
        Ui.Dropdown.Top.Start[
            Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Above"],
            Ui.Navmenu[
                Ui.NavmenuItem.Href("#account")["Account"],
                Ui.NavmenuItem.Href("#profile")["Profile"],
                Ui.NavmenuItem.Href("#billing")["Billing"],
                Ui.NavmenuItem.Href("#logout")["Logout"]
            ]
        ];
}
