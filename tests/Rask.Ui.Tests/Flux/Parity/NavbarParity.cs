using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/navbar</c>, example for example: the navbar, then the navlist and its groups.
/// </summary>
/// <remarks>
///     Every link on that page goes to <c>#</c>, and Flux marks the first of each example current. A string
///     href has no route to be compared with, so here the first says so itself.
/// </remarks>
public sealed partial class NavbarParity : FluxParity
{
    public override string Page => "navbar";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Row(Raw.Value(NavigationStandIns.Sheet), Div[Ui.Navbar[
            Ui.NavbarItem.Href("#").Current(true)["Home"],
            Ui.NavbarItem.Href("#")["Features"],
            Ui.NavbarItem.Href("#")["Pricing"],
            Ui.NavbarItem.Href("#")["About"]
        ]]));

        yield return ("with-icons", Row(Div[Ui.Navbar[
            Ui.NavbarItem.Href("#").Icon(Ui.IconName.Home).Current(true)["Home"],
            Ui.NavbarItem.Href("#").Icon(Ui.IconName.PuzzlePiece)["Features"],
            Ui.NavbarItem.Href("#").Icon(Ui.IconName.CurrencyDollar)["Pricing"],
            Ui.NavbarItem.Href("#").Icon(Ui.IconName.User)["About"]
        ]]));

        yield return ("with-badges", Row(Div[Ui.Navbar[
            Ui.NavbarItem.Href("#").Current(true)["Home"],
            Ui.NavbarItem.Href("#").Badge("12")["Inbox"],
            Ui.NavbarItem.Href("#")["Contacts"],
            Ui.NavbarItem.Href("#").Badge("Pro").BadgeColor(Ui.Color.Lime)["Calendar"]
        ]]));

        // The dropdown is another page's component: one box around the item that opens it. The item itself —
        // a button, with its trailing chevron — is the navbar's, and is what gives the box its size.
        yield return ("dropdown-navigation", Row(Div[Ui.Navbar[
            Ui.NavbarItem.Href("#").Current(true)["Dashboard"],
            Ui.NavbarItem.Href("#")["Transactions"],
            NavigationStandIns.Skipped("dropdown", "display:flex",
                Ui.NavbarItem.IconTrailing(Ui.IconName.ChevronDown)["Account"])
        ]]));

        yield return ("navlist-(sidebar)", Row(Div[Ui.Navlist.Class("w-64")[
            Ui.NavlistItem.Href("#").Icon(Ui.IconName.Home).Current(true)["Home"],
            Ui.NavlistItem.Href("#").Icon(Ui.IconName.PuzzlePiece)["Features"],
            Ui.NavlistItem.Href("#").Icon(Ui.IconName.CurrencyDollar)["Pricing"],
            Ui.NavlistItem.Href("#").Icon(Ui.IconName.User)["About"]
        ]]));

        yield return ("navlist-group", Row(Div[Ui.Navlist.Class("w-64")[
            Ui.NavlistGroup.Heading("Account").Class("mt-4")[
                Ui.NavlistItem.Href("#").Current(true)["Profile"],
                Ui.NavlistItem.Href("#")["Settings"],
                Ui.NavlistItem.Href("#")["Billing"]
            ]
        ]]));

        yield return ("collapsible-groups", Row(Div[Ui.Navlist.Class("w-64")[
            Ui.NavlistItem.Href("#").Icon(Ui.IconName.Home).Current(true)["Dashboard"],
            Ui.NavlistItem.Href("#").Icon(Ui.IconName.ListBullet)["Transactions"],
            Ui.NavlistGroup.Heading("Account").Expandable()[
                Ui.NavlistItem.Href("#")["Profile"],
                Ui.NavlistItem.Href("#")["Settings"],
                Ui.NavlistItem.Href("#")["Billing"]
            ]
        ]]));

        yield return ("navlist-badges", Row(Div[Ui.Navlist.Class("w-64")[
            Ui.NavlistItem.Href("#").Icon(Ui.IconName.Home).Current(true)["Home"],
            Ui.NavlistItem.Href("#").Icon(Ui.IconName.Envelope).Badge("12")["Inbox"],
            Ui.NavlistItem.Href("#").Icon(Ui.IconName.UserGroup)["Contacts"],
            Ui.NavlistItem.Href("#").Icon(Ui.IconName.CalendarDays).Badge("Pro").BadgeColor(Ui.Color.Lime)["Calendar"]
        ]]));
    }
}
