namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class NavbarIconsExample : Component
{
    protected override Component? Render() =>
        Ui.Navbar[
            Ui.NavbarItem.Key("home").Href("#").Icon(Ui.IconName.Home).Current(true)["Home"],
            Ui.NavbarItem.Key("features").Href("#").Icon(Ui.IconName.PuzzlePiece)["Features"],
            Ui.NavbarItem.Key("pricing").Href("#").Icon(Ui.IconName.CurrencyDollar)["Pricing"],
            Ui.NavbarItem.Key("about").Href("#").Icon(Ui.IconName.User)["About"]
        ];
}
