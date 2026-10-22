namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class HeaderSecondarySidebarExample : Component
{
    // The page's body: whatever holds Ui.Main is the layout grid, and the three are siblings in it.
    protected override Component? Render() =>
        Div.Class("min-h-screen bg-white antialiased dark:bg-zinc-800")[
            Bar(),
            Navigation(),
            Ui.Main.Container(true)[
                Div.Class("flex items-start max-md:flex-col")[
                    Div.Class("me-10 w-full pb-4 md:w-[220px]")[
                        Ui.Navlist[
                            Ui.NavlistItem.Href("#").Current(true)["Dashboard"],
                            Ui.NavlistItem.Href("#").Badge("32")["Orders"],
                            Ui.NavlistItem.Href("#")["Catalog"],
                            Ui.NavlistItem.Href("#")["Payments"],
                            Ui.NavlistItem.Href("#")["Customers"],
                            Ui.NavlistItem.Href("#")["Billing"],
                            Ui.NavlistItem.Href("#")["Quotes"],
                            Ui.NavlistItem.Href("#")["Configuration"]
                        ]
                    ],
                    Ui.Separator.Class("md:hidden"),
                    Div.Class("flex-1 self-stretch max-md:pt-6")[
                        Ui.Heading.Level(1).Xl["Good afternoon, Mara"],
                        Ui.Text.Class("mt-2 mb-6 text-base")["Here's what's new today"],
                        Ui.Separator.Subtle
                    ]
                ]
            ]
        ];

    private static Component Bar() =>
        Ui.Header.Container(true).Class("border-b border-zinc-200 bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-900")[
            Ui.SidebarToggle.Icon(Ui.IconName.Bars2),
            Ui.Brand.Href("#").Logo("/img/rask-mark.svg").Name("Acme Inc.").Class("max-lg:hidden"),
            Ui.Navbar.Class("-mb-px max-lg:hidden")[
                Ui.NavbarItem.Icon(Ui.IconName.Home).Href("#").Current(true)["Home"],
                Ui.NavbarItem.Icon(Ui.IconName.Inbox).Badge("12").Href("#")["Inbox"],
                Ui.NavbarItem.Icon(Ui.IconName.DocumentText).Href("#")["Documents"],
                Ui.NavbarItem.Icon(Ui.IconName.Calendar).Href("#")["Calendar"],
                Ui.Separator.Vertical().Subtle.Class("my-2"),
                Ui.Dropdown.Class("max-lg:hidden")[
                    Ui.NavbarItem.IconTrailing(Ui.IconName.ChevronDown)["Favorites"],
                    Ui.Navmenu[
                        Ui.NavmenuItem.Href("#")["Marketing site"],
                        Ui.NavmenuItem.Href("#")["Android app"],
                        Ui.NavmenuItem.Href("#")["Brand guidelines"]
                    ]
                ]
            ],
            Ui.Spacer,
            Ui.Navbar.Class("me-4")[
                Ui.NavbarItem.Icon(Ui.IconName.MagnifyingGlass).Href("#").Label("Search"),
                Ui.NavbarItem.Icon(Ui.IconName.Cog6Tooth).Href("#").Class("max-lg:hidden").Label("Settings"),
                Ui.NavbarItem.Icon(Ui.IconName.InformationCircle).Href("#").Class("max-lg:hidden").Label("Help")
            ],
            Ui.Dropdown.Position(Ui.DropdownPosition.Top).Align(Ui.DropdownAlign.Start)[
                Ui.Profile.Avatar("/img/avatars/amber.svg"),
                Account()
            ]
        ];

    // The header's links again, for a phone: lg:hidden keeps it off a desktop.
    private static Component Navigation() =>
        Ui.Sidebar.Sticky(true).Collapsible(Ui.SidebarCollapsible.Mobile)
            .Class("border-r border-zinc-200 bg-zinc-50 lg:hidden dark:border-zinc-700 dark:bg-zinc-900")[
            Ui.SidebarHeader[
                Ui.SidebarBrand.Href("#").Logo("/img/rask-mark.svg").Name("Acme Inc."),
                Ui.SidebarCollapse.Class("sidebar-desktop:-mr-2 sidebar-rail:mr-0")
            ],
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
