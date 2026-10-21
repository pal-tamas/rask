using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     The UI tab: Rask.Ui's own docs, at <c>/ui</c>, with a sidebar of their own beside the site's bar.
/// </summary>
/// <remarks>
///     The same shell the guides wear — the site's bar, a sidebar docked from md up that slides over the page below
///     it — drawn with Flux's sidebar and navlist. Every row is a link the router lights itself, and the drawer is the
///     sidebar's checkbox, which the runtime unchecks when a navigation completes, so nothing here holds state.
/// </remarks>
[Route("/ui")]
public sealed partial class UiDocsLayout : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
    [
        SiteHeader
            .FullBleed(true)
            .UiTabCurrent(true)
            .Leading(Ui.SidebarToggle
                .Icon(Ui.IconName.Bars3)
                .Class("hamburger-btn size-11")),
        Div.Class("app-shell")[
            Ui.Sidebar
                .Sticky(true)
                .Collapsible(Ui.SidebarCollapsible.Mobile)
                .Breakpoint(Ui.Breakpoint.Md)
                .Class("ui-docs-nav w-72! gap-2! overflow-hidden! border-e border-ui-line bg-ui-bg px-3! "
                       + "pt-[calc(var(--nav-h)+env(safe-area-inset-top))]! md:top-(--nav-h)! "
                       + "md:h-[calc(100dvh-var(--nav-h))] md:max-h-[calc(100dvh-var(--nav-h))]! md:w-[260px]! md:pt-6!")[
                Ui.Navlist.Class("min-h-0 flex-1 gap-6 overflow-y-auto overscroll-contain pb-8")[Groups()]
            ],
            Ui.Main.Class("min-w-0 px-4! py-6! pb-[calc(3rem_+_env(safe-area-inset-bottom))]! md:px-10!")[
                Main.Class("page-main mx-auto max-w-[920px]")[Outlet]
            ]
        ],
        // Once, for every page of the tab: where an action's .Confirm("…") asks, in place of `confirm`.
        Ui.Confirm
    ];

    private static IEnumerable<Component> Groups() =>
        UiDocsCatalog.All
            .GroupBy(entry => entry.Group, StringComparer.Ordinal)
            .Select(group => Ui.NavlistGroup.Key(group.Key).Heading(group.Key)[
                group.Select(entry => Ui.NavlistItem
                    .Key(entry.Path.ToString())
                    .Href(PageMeta.LinkTo(entry.Path))
                    .Class("ui-docs-link")[entry.Title])
            ]);
}
