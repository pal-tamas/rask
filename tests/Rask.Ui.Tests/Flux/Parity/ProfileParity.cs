using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/profile</c>, example for example.
/// </summary>
/// <remarks>
///     <para>
///     The separators between two profiles are the separator's page, and are left out. Flux's markdown lists
///     four profiles under "Avatar with initials"; its page draws two, and those two are here.
///     </para>
///     <para>
///     The two examples at the end sit in Flux's header and sidebar and open a dropdown. The bars are plain
///     boxes of the same measure, compared in full with what is in them; each dropdown is one box around the
///     profile that opens it.
///     </para>
/// </remarks>
public sealed partial class ProfileParity : FluxParity
{
    public override string Page => "profile";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Row(Raw.Value(NavigationStandIns.Sheet), Div[Ui.Profile.Avatar(NavigationStandIns.Caleb)]));

        yield return ("with-name", Row(Div[Ui.Profile.Name("Caleb Porzio").Avatar(NavigationStandIns.Caleb)]));

        yield return ("without-chevron", Row(Div[Ui.Profile.Chevron(false).Avatar(NavigationStandIns.Caleb)]));

        yield return ("circle-avatar", Row(
            Ui.Profile.Circle().Chevron(false).Avatar(NavigationStandIns.Caleb),
            Ui.Profile.Circle().Name("Caleb Porzio").Avatar(NavigationStandIns.Caleb)));

        yield return ("avatar-with-initials", Row(
            Ui.Profile.Name("Caleb Porzio"),
            Ui.Profile.AvatarName("Caleb Porzio").AvatarColor(Ui.Color.Cyan)));

        yield return ("custom-trailing-icon", Row(Div[
            Ui.Profile.IconTrailing(Ui.IconName.ChevronUpDown).Avatar(NavigationStandIns.Caleb).Name("Caleb Porzio")
        ]));

        yield return ("examples", Row(Div[
            Header.Class("stand-in-bar").Style("z-index:0").Attributes(("data-ui-header", null))[
                Brand(),
                Ui.Navbar[
                    Ui.NavbarItem.Href("#").Icon(Ui.IconName.Home).Current(true)["Home"],
                    Ui.NavbarItem.Href("#").Icon(Ui.IconName.Inbox).Badge("12")["Inbox"]
                ],
                Div.Style("width:96px"),
                NavigationStandIns.Skipped("dropdown", "display:flex", Ui.Profile.Avatar(NavigationStandIns.Caleb))
            ]
        ]));

        yield return ("examples", Row(Div[
            Div.Class("stand-in-side").Style("height:298px").Attributes(("data-ui-sidebar", null))[
                Brand().Class("px-2"),
                Ui.Navlist.Variant(Ui.NavlistVariant.Outline)[
                    Ui.NavlistItem.Href("#").Icon(Ui.IconName.Home).Current(true)["Home"],
                    Ui.NavlistItem.Href("#").Icon(Ui.IconName.Inbox).Badge("12")["Inbox"],
                    Ui.NavlistItem.Href("#").Icon(Ui.IconName.DocumentText)["Documents"],
                    Ui.NavlistItem.Href("#").Icon(Ui.IconName.Calendar)["Calendar"]
                ],
                Div.Style("flex:1 1 0%").Attributes(("data-ui-spacer", null)),
                Ui.Dropdown.Top.Start[
                    Ui.Profile.Name("Caleb Porzio").Avatar(NavigationStandIns.Caleb).Class("w-full"),
                    Ui.Menu[
                        Ui.MenuRadioGroup.Value("Caleb Porzio")[
                            Ui.MenuRadio.Value("Caleb Porzio")["Caleb Porzio"],
                            Ui.MenuRadio.Value("Hugo Sainte-Marie")["Hugo Sainte-Marie"],
                            Ui.MenuRadio.Value("Josh Hanley")["Josh Hanley"]
                        ],
                        Ui.MenuSeparator,
                        Ui.MenuItem.Icon(Ui.IconName.ArrowRightStartOnRectangle)["Logout"]
                    ]
                ]
            ]
        ]));
    }

    // On this page Flux's brand holds its mark in a tile of its own inside the logo's box.
    private static UiBrand Brand() =>
        Ui.Brand.Href("#").Name("Acme Inc.").Logo(
            Div.Class("logo-tile logo-accent")[I.Style(NavigationStandIns.Bold)["A"]]);
}
