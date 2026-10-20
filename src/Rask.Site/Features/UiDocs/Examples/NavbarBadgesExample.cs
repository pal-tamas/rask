namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class NavbarBadgesExample : Component
{
    protected override Component? Render() =>
        Ui.Navbar[
            Ui.NavbarItem.Key("home").Href("#").Current(true)["Home"],
            Ui.NavbarItem.Key("inbox").Href("#").Badge("12")["Inbox"],
            Ui.NavbarItem.Key("contacts").Href("#")["Contacts"],
            Ui.NavbarItem.Key("calendar").Href("#").Badge("Pro").BadgeColor(Ui.Color.Lime)["Calendar"]
        ];
}
