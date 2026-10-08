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
            + "steps, breadcrumbs and pagination.",
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
            .Files(["UiKitTabsDemo.cs", "UiKitNavigationDemo.cs"])
            .Notes("Only two of the tab rows hold their selected tab in the page; the rest keep track themselves. "
                + "Every crumb and page number is an <a href>.")
            .Result(UiKitNavigationDemo)
    ];
}
