using Rask.Core.Routing;

namespace Rask.Site.Features.UiKit;

/// <summary>Flux's sidebar layout as a whole page: it cannot be shown inside one.</summary>
/// <remarks>
///     Outside the docs layout on purpose. The sidebar docks from <c>lg</c> up and narrows to a rail of icons;
///     below it the header's toggle slides it over the page. Nothing here runs a script: both states are checkboxes.
/// </remarks>
[Route("demo/sidebar")]
public sealed partial class SidebarLayoutDemoPage : Component
{
    private const string Ground = "bg-zinc-50 dark:bg-zinc-900";

    /// <inheritdoc />
    protected override Component? HeadAssets =>
        PageMeta.For(
            "Sidebar layout demo — Rask UI kit",
            "Flux UI's sidebar layout in C#: a sticky sidebar that narrows to a rail of icons and slides over "
            + "the page on a phone, with no script.",
            Routes.SidebarLayoutDemoPage());

    /// <inheritdoc />
    protected override Component? Render() =>
        // Whatever holds Ui.Main is the layout grid. In an app that is the body; here, the page's own ground.
        Div.Data("testid", "sidebar-layout-demo").Class("min-h-dvh bg-white antialiased dark:bg-zinc-800")[
            Ui.Sidebar.Sticky().Collapsible(Ui.SidebarCollapsible.Always)
                .Class(Ground + " border-r border-zinc-200 dark:border-zinc-700")[
                Ui.SidebarHeader[
                    Ui.SidebarBrand.Href(Routes.HomePage()).Logo("/img/rask-mark.svg").Name("Rask"),
                    Ui.SidebarCollapse
                ],
                Ui.SidebarSearch.Placeholder("Search..."),
                Ui.SidebarNav[
                    Ui.SidebarItem.Icon(Ui.IconName.Home).Href(Routes.SidebarLayoutDemoPage()).Tooltip("Home")["Home"],
                    Ui.SidebarItem.Icon(Ui.IconName.Inbox).Badge("12").Href(Routes.HeaderLayoutDemoPage()).Tooltip("Header layout")[
                        "Header layout"
                    ],
                    Ui.SidebarItem.Icon(Ui.IconName.BookOpen).Href(Routes.UiKitLayoutPage()).Tooltip("Back to the docs")[
                        "Back to the docs"
                    ],
                    Ui.SidebarGroup.Expandable().Icon(Ui.IconName.Star).Heading("Favorites")[
                        Ui.SidebarItem.Href(Routes.UiKitActionsPage())["Actions"],
                        Ui.SidebarItem.Href(Routes.UiKitNavigationPage())["Navigation"]
                    ]
                ],
                Ui.SidebarSpacer,
                Ui.SidebarNav[
                    Ui.SidebarItem.Icon(Ui.IconName.InformationCircle).Href(Routes.GuidesIndexPage()).Tooltip("Guides")["Guides"]
                ],
                Ui.SidebarProfile.Name("Ada Lovelace")
            ],
            Ui.Header.Class("lg:hidden")[
                Ui.SidebarToggle.Inset(Ui.Position.Left),
                Ui.Spacer
            ],
            Ui.Main[
                H1.Class("text-2xl font-medium text-zinc-800 dark:text-white")["Good afternoon, Ada"],
                P.Class("mt-2 mb-6 text-zinc-500 dark:text-white/70")[
                    "This page is Ui.Sidebar, Ui.Header and Ui.Main. Narrow the window, or press the control beside the brand."
                ],
                Hr.Class("border-zinc-800/5 dark:border-white/10")
            ]
        ];
}
