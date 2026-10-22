namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SidebarInsetExample : Component
{
    // The page's body: whatever holds Ui.Main is the layout grid, and the three are siblings in it.
    protected override Component? Render() =>
        Div.Class("min-h-dvh bg-zinc-50 dark:bg-zinc-900")[
            Navigation(),
            Bar(),
            Ui.Main.Inset(true).Class("lg:ms-0")[
                Ui.Heading.Level(1).Xl["Good afternoon, Mara"],
                Ui.Text.Class("mt-2 mb-6 text-base")["Here's what's new today"],
                Ui.Separator.Subtle
            ]
        ];

    private static Component Navigation() =>
        Ui.Sidebar.Sticky(true).Collapsible(Ui.SidebarCollapsible.Always)
            .Class("bg-zinc-50 dark:bg-zinc-900")[
            Ui.SidebarHeader[
                Ui.SidebarBrand.Href("#").Logo("/img/rask-mark.svg").Name("Acme Inc."),
                Ui.SidebarCollapse.Class("sidebar-desktop:-mr-2 sidebar-rail:mr-0")
            ],
            Ui.SidebarNav[
                Ui.SidebarItem.Icon(Ui.IconName.Home).Href("#").Current(true)["Home"],
                Ui.SidebarItem.Icon(Ui.IconName.Inbox).Badge("12").Href("#")["Inbox"],
                Ui.SidebarItem.Icon(Ui.IconName.DocumentText).Href("#")["Documents"],
                Ui.SidebarItem.Icon(Ui.IconName.Calendar).Href("#")["Calendar"],
                Ui.SidebarGroup.Expandable(true).Icon(Ui.IconName.Star).Heading("Favorites")[
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

    // A phone's bar: the toggle that slides the sidebar in, and the account.
    private static Component Bar() =>
        Ui.Header.Class("bg-white lg:hidden dark:bg-zinc-800")[
            Ui.SidebarToggle.Icon(Ui.IconName.Bars2).Inset(Ui.Position.Left),
            Ui.Spacer,
            Ui.Dropdown.Position(Ui.DropdownPosition.Top).Align(Ui.DropdownAlign.Start)[
                Ui.Profile.Avatar("/img/avatars/amber.svg"),
                Account()
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
