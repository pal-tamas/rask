namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class NavbarExample : Component
{
    protected override Component? Render() =>
        Ui.Navbar[
            Ui.NavbarItem.Key("home").Href("#").Current(true)["Home"],
            Ui.NavbarItem.Key("features").Href("#")["Features"],
            Ui.NavbarItem.Key("pricing").Href("#")["Pricing"],
            Ui.NavbarItem.Key("about").Href("#")["About"]
        ];
}
