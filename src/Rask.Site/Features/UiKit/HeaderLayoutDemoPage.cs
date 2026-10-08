using Rask.Core.Routing;

namespace Rask.Site.Features.UiKit;

/// <summary>Flux's header layout as a whole page: it cannot be shown inside one.</summary>
/// <remarks>
///     A full-width header holds the navigation from <c>lg</c> up. Below it the links move into a sidebar
///     that the header's toggle slides over the page.
/// </remarks>
[Route("demo/header")]
public sealed partial class HeaderLayoutDemoPage : Component
{
    private const string Ground = "bg-zinc-50 dark:bg-zinc-900";
    private const string Link = "rounded-lg px-3 py-1.5 text-sm font-medium text-zinc-500 hover:bg-zinc-800/5 "
                                + "hover:text-zinc-800 max-lg:hidden dark:text-white/80 dark:hover:bg-white/10 dark:hover:text-white";

    /// <inheritdoc />
    protected override Component? HeadAssets =>
        PageMeta.For(
            "Header layout demo — Rask UI kit",
            "Flux UI's header layout in C#: a full-width header with the navigation, a container-width main, and "
            + "a sidebar that takes the links on a phone — with no script.",
            Routes.HeaderLayoutDemoPage());

    /// <inheritdoc />
    protected override Component? Render() =>
        // Whatever holds Ui.Main is the layout grid. In an app that is the body; here, the page's own ground.
        Div.Data("testid", "header-layout-demo").Class("min-h-dvh bg-white antialiased dark:bg-zinc-800")[
            Ui.Header.Container().Class(Ground + " border-b border-zinc-200 dark:border-zinc-700")[
                Ui.SidebarToggle.Inset(Ui.Position.Left),
                NavLink.Href(Routes.HomePage()).ActiveClass("").Class("me-4 text-sm font-medium text-zinc-800 max-lg:hidden dark:text-white")["Rask"],
                NavLink.Href(Routes.SidebarLayoutDemoPage()).ActiveClass("").Class(Link)["Sidebar layout"],
                NavLink.Href(Routes.UiKitLayoutPage()).ActiveClass("").Class(Link)["Back to the docs"],
                Ui.Spacer
            ],
            Ui.Sidebar.Sticky().Collapsible(Ui.SidebarCollapsible.Mobile)
                .Class("lg:hidden " + Ground + " border-r border-zinc-200 dark:border-zinc-700")[
                Ui.SidebarHeader[
                    Ui.SidebarBrand.Href(Routes.HomePage()).Logo("/img/rask-mark.svg").Name("Rask"),
                    Ui.SidebarCollapse
                ],
                Ui.SidebarNav[
                    Ui.SidebarItem.Icon(Ui.IconName.Home).Href(Routes.HeaderLayoutDemoPage())["Home"],
                    Ui.SidebarItem.Icon(Ui.IconName.Squares2x2).Href(Routes.SidebarLayoutDemoPage())["Sidebar layout"],
                    Ui.SidebarItem.Icon(Ui.IconName.BookOpen).Href(Routes.UiKitLayoutPage())["Back to the docs"]
                ]
            ],
            Ui.Main.Container()[
                H1.Class("text-2xl font-medium text-zinc-800 dark:text-white")["Good afternoon, Ada"],
                P.Class("mt-2 mb-6 text-zinc-500 dark:text-white/70")[
                    "This page is Ui.Header, Ui.Sidebar and Ui.Main. Narrow the window: the links move into the sidebar."
                ],
                Hr.Class("border-zinc-800/5 dark:border-white/10")
            ]
        ];
}
