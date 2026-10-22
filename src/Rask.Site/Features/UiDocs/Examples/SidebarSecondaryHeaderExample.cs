namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SidebarSecondaryHeaderExample : Component
{
    // The page's body: whatever holds Ui.Main is the layout grid, and the three are siblings in it.
    protected override Component? Render() =>
        Div.Class("min-h-screen bg-white antialiased dark:bg-zinc-800")[
            Navigation(),
            Bar(),
            Ui.Main[
                Ui.Heading.Level(1).Xl["Good afternoon, Mara"],
                Ui.Text.Class("mt-2 mb-6 text-base")["Here's what's new today"],
                Ui.Separator.Subtle
            ]
        ];

    private static Component Navigation() =>
        Ui.Sidebar.Sticky(true).Collapsible(Ui.SidebarCollapsible.Mobile)
            .Class("border-r border-zinc-200 bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-900")[
            Ui.SidebarHeader[
                Ui.SidebarBrand.Href("#").Logo("/img/rask-mark.svg").Name("Acme Inc."),
                Ui.SidebarCollapse.Class("lg:hidden")
            ],
            Ui.SidebarSearch.Placeholder("Search..."),
            Ui.SidebarNav[
                Ui.SidebarItem.Icon(Ui.IconName.Home).Href("#").Current(true)["Home"],
                Ui.SidebarItem.Icon(Ui.IconName.Inbox).Badge("12").Href("#")["Inbox"],
                Ui.SidebarItem.Icon(Ui.IconName.DocumentText).Href("#")["Documents"],
                Ui.SidebarItem.Icon(Ui.IconName.Calendar).Href("#")["Calendar"],
                Ui.SidebarGroup.Expandable(true).Heading("Favorites")[
                    Ui.SidebarItem.Href("#")["Marketing site"],
                    Ui.SidebarItem.Href("#")["Android app"],
                    Ui.SidebarItem.Href("#")["Brand guidelines"]
                ]
            ],
            Ui.SidebarSpacer,
            Ui.SidebarNav[
                Ui.SidebarItem.Icon(Ui.IconName.Cog6Tooth).Href("#")["Settings"],
                Ui.SidebarItem.Icon(Ui.IconName.InformationCircle).Href("#")["Help"]
            ],
            Ui.Dropdown.Position(Ui.DropdownPosition.Top).Align(Ui.DropdownAlign.Start).Class("flex max-lg:hidden")[
                Ui.SidebarProfile.Avatar("/img/avatars/amber.svg").Name("Mara Lind"),
                Account()
            ]
        ];

    // Two navbars: a phone's bar, and the section's own links, which stay at every width.
    private static Component Bar() =>
        Ui.Header.Class("block! border-b border-zinc-200 bg-white lg:bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-900")[
            Ui.Navbar.Class("w-full lg:hidden")[
                Ui.SidebarToggle.Icon(Ui.IconName.Bars2).Inset(Ui.Position.Left),
                Ui.Spacer,
                Ui.Dropdown.Position(Ui.DropdownPosition.Top).Align(Ui.DropdownAlign.Start)[
                    Ui.Profile.Avatar("/img/avatars/amber.svg"),
                    Account()
                ]
            ],
            // Flux's navbar scrollable: a row that scrolls sideways rather than pushing a phone's page wider.
            Div.Class("overflow-x-auto [scrollbar-width:none]")[
                Ui.Navbar.Class("w-max")[
                    Ui.NavbarItem.Href("#").Current(true)["Dashboard"],
                    Ui.NavbarItem.Badge("32").Href("#")["Orders"],
                    Ui.NavbarItem.Href("#")["Catalog"],
                    Ui.NavbarItem.Href("#")["Configuration"]
                ]
            ]
        ];

    private static Component Account() =>
        Ui.Menu[
            Ui.MenuRadioGroup.Value("Mara Lind")[
                Ui.MenuRadio.Value("Mara Lind")["Mara Lind"],
                Ui.MenuRadio.Value("Theo Brandt")["Theo Brandt"]
            ],
            Ui.MenuSeparator,
            Ui.MenuItem.Icon(Ui.IconName.ArrowRightStartOnRectangle)["Logout"]
        ];
}
