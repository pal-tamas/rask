namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DropdownOffsetExample : Component
{
    protected override Component? Render() =>
        Ui.Dropdown.Offset(-15).Gap(2)[
            Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Offset and gap"],
            Ui.Navmenu[
                Ui.NavmenuItem.Href("#account")["Account"],
                Ui.NavmenuItem.Href("#profile")["Profile"],
                Ui.NavmenuItem.Href("#billing")["Billing"],
                Ui.NavmenuItem.Href("#logout")["Logout"]
            ]
        ];
}
