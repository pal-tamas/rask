using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>What Flux's layout demos share: one sidebar, one greeting, and the classes the demos write themselves.</summary>
internal sealed partial class LayoutDemo : global::Rask.Core.RaskMarkup
{
    public const string Logo = "https://fluxui.dev/img/demo/logo.png";
    public const string DarkLogo = "https://fluxui.dev/img/demo/dark-mode-logo.png";
    public const string User = "https://fluxui.dev/img/demo/user.png";

    public const string Ground = "bg-zinc-50 dark:bg-zinc-900";
    public const string Edge = "border-zinc-200 dark:border-zinc-700";

    /// <summary>
    ///     The demos' own classes, as an app's Tailwind build would emit them (the kit's sheet holds only what
    ///     the kit writes), and the boxes that stand where another component goes, at the size Flux gave it.
    /// </summary>
    public const string Utilities =
        "body.min-h-screen{min-height:100vh}body.min-h-dvh{min-height:100dvh}.antialiased{-webkit-font-smoothing:antialiased}"
        // On the body and the header only, which is where the demos write it: the kit's menu says `bg-white` too,
        // and an unlayered rule for every `.bg-white` would beat its `dark:` ground. In `:where()`, so it weighs
        // what a utility weighs and the header's `lg:bg-zinc-50` after it still wins.
        + ":where(body,[data-ui-header]).bg-white{background-color:#fff}.bg-zinc-50{background-color:oklch(98.5% 0 0)}"
        + ".dark .dark\\:bg-zinc-800{background-color:oklch(27.4% .006 286.033)}"
        + ".dark .dark\\:bg-zinc-900,.dark.dark\\:bg-zinc-900{background-color:oklch(21% .006 285.885)}"
        + ".dark.dark\\:bg-zinc-800{background-color:oklch(27.4% .006 286.033)}"
        + ".border-r{border-right-width:1px}.border-b{border-bottom-width:1px}"
        + ".border-zinc-200{border-color:oklch(92% .004 286.32)}.dark .dark\\:border-zinc-700{border-color:oklch(37% .013 285.805)}"
        + ".block\\!{display:block!important}.w-full{width:100%}"
        + "@media (min-width:64rem){.lg\\:hidden{display:none!important}.lg\\:ms-0{margin-inline-start:0}.lg\\:mt-0{margin-top:0}"
        + ".lg\\:bg-zinc-50{background-color:oklch(98.5% 0 0)}"
        // Flux: in-data-flux-sidebar-on-desktop:not-in-data-flux-sidebar-collapsed-desktop:-mr-2
        + "[data-ui-sidebar]:not(:has(> [data-ui-sidebar-rail]:checked)) .docked\\:-mr-2{margin-right:-.5rem}}"
        + "@media (max-width:63.99rem){.max-lg\\:hidden{display:none!important}}"
        // Slots: a brand, the two navbars of the header demos, a profile, the greeting.
        + ".slot-brand{width:98.9px;height:40px;margin-right:16px;flex-shrink:0}"
        + ".slot-navbar{width:585.9px;height:56px;margin-bottom:-1px;flex-shrink:0}"
        + ".slot-actions{width:40px;height:56px;margin-right:16px;flex-shrink:0}"
        + "@media (min-width:64rem){.slot-actions{width:124px}}"
        + ".slot-profile{width:72px;height:40px}.slot-heading{height:32px}.slot-text{height:24px;margin:8px 0 24px}"
        + ".slot-separator{height:1px;width:100%}.slot-tabs{height:56px}.slot-bar{height:64px;width:100%}"
        + ".slot-navlist{height:336px}@media (min-width:64rem){.slot-navlist{height:272px}}"
        // The secondary sidebar's own row, written by the demo beside its navlist.
        + ".demo-row{display:flex;align-items:flex-start}.demo-side{width:100%;padding-bottom:16px;margin-inline-end:40px}"
        + ".demo-body{flex:1 1 0%;align-self:stretch}"
        + "@media (min-width:48rem){.demo-side{width:220px}.md\\:hidden{display:none}}"
        + "@media (max-width:47.99rem){.demo-row{flex-direction:column}.demo-body{padding-top:24px}}";

    /// <summary>The sidebar every demo has. <paramref name="star" />: the Favorites group carries an icon.</summary>
    public static Component Sidebar(
        Ui.SidebarCollapsible collapsible, string classes, string collapse, bool search, bool star, bool profile) =>
        Ui.Sidebar.Sticky(true).Collapsible(collapsible).Class(classes)[
            Ui.SidebarHeader[
                Ui.SidebarBrand.Href("#").Logo(Logo).LogoDark(DarkLogo).Name("Acme Inc."),
                Ui.SidebarCollapse.Class(collapse)
            ],
            search ? Ui.SidebarSearch.Placeholder("Search...") : null,
            Ui.SidebarNav[
                Ui.SidebarItem.Icon(Ui.IconName.Home).Href("#").Current(true)["Home"],
                Ui.SidebarItem.Icon(Ui.IconName.Inbox).Badge("12").Href("#")["Inbox"],
                Ui.SidebarItem.Icon(Ui.IconName.DocumentText).Href("#")["Documents"],
                Ui.SidebarItem.Icon(Ui.IconName.Calendar).Href("#")["Calendar"],
                Favorites(star)
            ],
            Ui.SidebarSpacer,
            Ui.SidebarNav[
                Ui.SidebarItem.Icon(Ui.IconName.Cog6Tooth).Href("#")["Settings"],
                Ui.SidebarItem.Icon(Ui.IconName.InformationCircle).Href("#")["Help"]
            ],
            profile
                ? Ui.Dropdown.Position(Ui.DropdownPosition.Top).Align(Ui.DropdownAlign.Start).Class("flex max-lg:hidden")[
                    Ui.SidebarProfile.Avatar(User).Name("Olivia Martin"),
                    Account()
                ]
                : null
        ];

    private static readonly Ui.IconName[] GroupIcons =
    [
        Ui.IconName.Star, Ui.IconName.Folder, Ui.IconName.ChartBar, Ui.IconName.Users, Ui.IconName.CreditCard,
        Ui.IconName.Truck, Ui.IconName.Tag, Ui.IconName.Wrench, Ui.IconName.Bell, Ui.IconName.Key,
        Ui.IconName.GlobeAlt, Ui.IconName.ArchiveBox,
    ];

    /// <summary>
    ///     No demo of Flux's: an application's menu — twelve groups, eighty items, three groups folded, one item
    ///     current deep in the list — in the collapsible demo's sidebar. <c>scripts/flux/rail.mjs</c> walks it.
    /// </summary>
    public static Component LongMenu(string classes) =>
        Ui.Sidebar.Sticky(true).Collapsible(Ui.SidebarCollapsible.Always).Class(classes)[
            Ui.SidebarHeader[
                Ui.SidebarBrand.Href("#").Logo(Logo).LogoDark(DarkLogo).Name("Acme Inc."),
                Ui.SidebarCollapse.Class("docked:-mr-2")
            ],
            Ui.SidebarNav[
                Ui.SidebarItem.Icon(Ui.IconName.Home).Href("#")["Home"],
                Ui.SidebarItem.Icon(Ui.IconName.Inbox).Badge("12").Href("#")["Inbox"],
                Ui.SidebarItem.Icon(Ui.IconName.DocumentText).Href("#")["Documents"],
                Ui.SidebarItem.Icon(Ui.IconName.Calendar).Href("#")["Calendar"],
                Enumerable.Range(0, GroupIcons.Length).Select(Group).ToArray()
            ],
            Ui.SidebarSpacer,
            Ui.SidebarNav[
                Ui.SidebarItem.Icon(Ui.IconName.Cog6Tooth).Href("#")["Settings"],
                Ui.SidebarItem.Icon(Ui.IconName.InformationCircle).Href("#")["Help"],
                Ui.SidebarItem.Icon(Ui.IconName.Lifebuoy).Href("#")["Support"],
                Ui.SidebarItem.Icon(Ui.IconName.ArrowRightStartOnRectangle).Href("#")["Sign out"]
            ],
            Ui.Dropdown.Position(Ui.DropdownPosition.Top).Align(Ui.DropdownAlign.Start).Class("flex max-lg:hidden")[
                Ui.SidebarProfile.Avatar(User).Name("Olivia Martin"),
                Account()
            ]
        ];

    // Six items a group; groups 3, 7 and 11 start folded, and the current page is the fourth item of group 9.
    private static Component Group(int index)
    {
        var number = index + 1;
        var group = Ui.SidebarGroup.Key(number).Expandable(true).Icon(GroupIcons[index]).Heading("Group " + number);
        return (number % 4 == 3 ? group.Expanded(false) : group)[
            Enumerable.Range(1, 6)
                .Select(item => (Component)Item(number, item))
                .ToArray()
        ];
    }

    private static Component Item(int group, int item)
    {
        var link = Ui.SidebarItem.Key(item).Href("#");
        return (group == 9 && item == 4 ? link.Current(true) : link)["Group " + group + " item " + item];
    }

    /// <summary>The phone's header in the sidebar demos: the toggle, a spacer, the account.</summary>
    public static Component[] Bar() =>
    [
        Ui.SidebarToggle.Icon(Ui.IconName.Bars2).Inset(Ui.Position.Left),
        Ui.Spacer,
        Ui.Dropdown.Position(Ui.DropdownPosition.Top).Align(Ui.DropdownAlign.Start)[Ui.Profile.Avatar(User), Account()]
    ];

    /// <summary>The account menu both profiles open.</summary>
    public static Component Account() =>
        Ui.Menu[
            Ui.MenuRadioGroup.Value("Olivia Martin")[
                Ui.MenuRadio.Value("Olivia Martin")["Olivia Martin"],
                Ui.MenuRadio.Value("Truly Delta")["Truly Delta"]
            ],
            Ui.MenuSeparator,
            Ui.MenuItem.Icon(Ui.IconName.ArrowRightStartOnRectangle)["Logout"]
        ];

    /// <summary>Heading, text and separator: three other components, three boxes.</summary>
    public static Component[] Greeting() =>
    [
        Div.Class("slot-heading"),
        Div.Class("slot-text"),
        Div.Class("slot-separator")
    ];

    private static Component Favorites(bool star)
    {
        var group = Ui.SidebarGroup.Expandable(true).Heading("Favorites");
        return (star ? group.Icon(Ui.IconName.Star) : group)[
            Ui.SidebarItem.Href("#")["Marketing site"],
            Ui.SidebarItem.Href("#")["Android app"],
            Ui.SidebarItem.Href("#")["Brand guidelines"]
        ];
    }
}
