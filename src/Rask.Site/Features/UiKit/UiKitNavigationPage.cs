using Rask.Core.Routing;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's Navigation components, live.
/// </summary>
[Route("ui/navigation")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitNavigationPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets =>
        PageMeta.For(
            "Tabs, nav lists, steps and breadcrumbs in C# — Rask",
            "Navigation components in C#: Flux-style tabs with panels, segmented and pill variants, a nav list, "
            + "steps, breadcrumbs and Flux UI's pagination.",
            Routes.UiKitNavigationPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Navigation"],
        P.Class("text-ui-muted")[
            "The tabs are Flux's — a row, a segmented control or pills, over panels or on their own — and ",
            "their selected tab is a value the page can bind. The rest of the category is links: every crumb ",
            "and page number is bookmarkable and answers the back button. A menu of links that opens from a ",
            "button is Ui.Navmenu in a Ui.Dropdown, on the Actions page."
        ],
        CodeSample
            .Files(["UiKitTabsDemo.cs", "UiKitNavigationDemo.cs", "UiKitPaginationDemo.cs"])
            .Notes("Only two of the tab rows hold their selected tab in the page; the rest keep track themselves. "
                + "Every crumb is an <a href>; each pager keeps the page it is on in a field.")
            .Result(UiKitNavigationDemo),
        H2.Class("text-2xl font-bold mt-10 mb-1")["Flux navigation"],
        P.Class("text-ui-muted")[
            "The navbar, navlist, brand, profile, breadcrumbs and avatar are Flux UI's, example for example: ",
            "the same names and props, measured against fluxui.dev in light and in dark."
        ],
        CodeSample
            .Files(["UiKitFluxNavigationDemo.cs"])
            .Notes("The current item is worked out from the route for a generated link and stated for a "
                + "string one; an expandable group is a <details>, so it folds with no runtime.")
            .Result(UiKitFluxNavigationDemo)
    ];
}
