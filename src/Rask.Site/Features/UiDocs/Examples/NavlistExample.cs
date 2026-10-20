namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class NavlistExample : Component
{
    protected override Component? Render() =>
        Ui.Navlist.Class("w-64")[
            Ui.NavlistItem.Key("home").Href("#").Icon(Ui.IconName.Home).Current(true)["Home"],
            Ui.NavlistItem.Key("features").Href("#").Icon(Ui.IconName.PuzzlePiece)["Features"],
            Ui.NavlistItem.Key("pricing").Href("#").Icon(Ui.IconName.CurrencyDollar)["Pricing"],
            Ui.NavlistItem.Key("about").Href("#").Icon(Ui.IconName.User)["About"]
        ];
}
