using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/profile</c>, example for example.
/// </summary>
/// <remarks>
///     <para>
///     Flux's markdown lists four profiles under "Avatar with initials"; its page draws two, and those two are here.
///     </para>
///     <para>
///     The two examples at the end sit in Flux's header and sidebar and open a dropdown: the real
///     <c>Ui.Header</c>, <c>Ui.Dropdown</c>, <c>Ui.Navmenu</c> and <c>Ui.Menu</c>. The sidebar is a box of the
///     same measure, as its navlist is (see that example).
///     </para>
/// </remarks>
public sealed partial class ProfileParity : FluxParity
{
    // What an app's own Tailwind build emits for the classes the profile menu's example hands to `class`, under
    // names of this page's own: unlayered, a real utility's name would beat the kit's `dark:` twin of it.
    private const string MenuUtilities =
        "<style>.parity-bar-under{z-index:0!important}.parity-w-24{width:6rem}.parity-max-w-48{max-width:12rem}"
        + ".parity-px-2-py-1{padding:.375rem .5rem}.parity-mt-1{margin-top:.25rem!important}.parity-pl-7{padding-left:1.75rem}"
        + ".parity-truncate{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}"
        + ".parity-ink{color:oklch(27.4% .006 286.033)}.parity-ink:where(.dark,.dark *){color:#fff}</style>";

    public override string Page => "profile";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Row(Raw.Value(NavigationStandIns.Sheet), Div[Ui.Profile.Avatar(NavigationStandIns.Caleb)]));

        yield return ("with-name", Row(Div[Ui.Profile.Name("Caleb Porzio").Avatar(NavigationStandIns.Caleb)]));

        yield return ("without-chevron", Row(Div[Ui.Profile.Chevron(false).Avatar(NavigationStandIns.Caleb)]));

        yield return ("circle-avatar", Div.Style("display:flex;justify-content:center")[Div.Style("display:flex;gap:24px")[
            Ui.Profile.Circle().Chevron(false).Avatar(NavigationStandIns.Caleb),
            Ui.Separator.Vertical().Class("my-2"),
            Ui.Profile.Circle().Name("Caleb Porzio").Avatar(NavigationStandIns.Caleb)
        ]]);

        // The page sets these two on a ground of their own, `bg-zinc-50 dark:bg-zinc-800 rounded-xl p-6 -my-6`.
        yield return ("avatar-with-initials", Div.Style("display:flex;justify-content:center")[
            Raw.Value("<style>.parity-ground{display:flex;gap:24px;align-items:flex-end;background:oklch(98.5% 0 0);"
                + "border-radius:.75rem;padding:24px;margin-block:-24px}"
                + ".parity-ground:where(.dark,.dark *){background:oklch(27.4% .006 286.033)}</style>"),
            Div.Class("parity-ground")[
                Ui.Profile.Name("Caleb Porzio"),
                Ui.Separator.Vertical().Subtle,
                Ui.Profile.AvatarName("Caleb Porzio").AvatarColor(Ui.Color.Cyan)
            ]
        ]);

        yield return ("custom-trailing-icon", Row(Div[
            Ui.Profile.IconTrailing(Ui.IconName.ChevronUpDown).Avatar(NavigationStandIns.Caleb).Name("Caleb Porzio")
        ]));

        // The profile menu, in Flux's header: its words, a team picked and one indented, the account's links.
        yield return ("examples", Row(Div[
            Raw.Value(MenuUtilities),
            Ui.Header.Class("parity-bar parity-bar-under")[
                Brand(),
                Ui.Navbar[
                    Ui.NavbarItem.Href("#").Icon(Ui.IconName.Home).Current(true)["Home"],
                    Ui.NavbarItem.Href("#").Icon(Ui.IconName.Inbox).Badge("12")["Inbox"]
                ],
                Div.Class("parity-w-24"),
                Ui.Dropdown.End[
                    Ui.Profile.Avatar(NavigationStandIns.Caleb),
                    Ui.Navmenu.Class("parity-max-w-48")[
                        Div.Class("parity-px-2-py-1")[
                            Ui.Text.Sm["Signed in as"],
                            Ui.Heading.Class("parity-mt-1 parity-truncate")["caleb@example.com"]
                        ],
                        Ui.NavmenuSeparator,
                        Div.Class("parity-px-2-py-1")[Ui.Text.Sm.Class("parity-pl-7")["Teams"]],
                        Ui.NavmenuItem.Href("#").Icon(Ui.IconName.Check).Class("parity-ink parity-truncate")["Personal"],
                        Ui.NavmenuItem.Href("#").Indent().Class("parity-ink parity-truncate")["Wireable LLC"],
                        Ui.NavmenuSeparator,
                        Ui.NavmenuItem.Href("/dashboard").Icon(Ui.IconName.Key).Class("parity-ink")["Licenses"],
                        Ui.NavmenuItem.Href("/account").Icon(Ui.IconName.User).Class("parity-ink")["Account"],
                        Ui.NavmenuSeparator,
                        Ui.NavmenuItem.Href("/logout").Icon(Ui.IconName.ArrowRightStartOnRectangle).Class("parity-ink")["Logout"]
                    ]
                ]
            ]
        ]));

        yield return ("examples", Row(Div[
            // The navlist's items are 40px tall below lg, 32px from it: its box grows on a phone.
            Raw.Value("<style>.parity-nav{height:136px}@media (width < 64rem){.parity-nav{height:168px}}</style>"),
            Ui.Sidebar.Class("parity-side")[
                Brand().Class("px-2"),
                // The docs page's own decor: a navlist drawn the way Flux's sidebar draws its items, by a
                // `variant` no page documents. It is a box here, and Ui.SidebarNav is what draws that look.
                Div.Class("parity-nav").Attributes(("data-ui-navlist", null), ("data-parity-skip", null)),
                Ui.Spacer,
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
