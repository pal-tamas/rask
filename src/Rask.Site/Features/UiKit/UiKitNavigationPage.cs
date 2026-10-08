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
            "Tabs, menus and megamenu in C# — Rask",
            "Navigation components in C#: Flux-style tabs with panels, segmented and pill variants, a megamenu "
            + "on native popovers, menus, steps and breadcrumbs.",
            Routes.UiKitNavigationPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Navigation"],
        P.Class("text-ui-muted")[
            "The megamenu is the browser's, deliberately: it is built on the native popover API, so it gets ",
            "the top layer, Escape and light-dismiss without a line of script. The tabs are Flux's — a row, ",
            "a segmented control or pills, over panels or on their own — and their selected tab is a value ",
            "the page can bind."
        ],
        CodeSample
            .Files(["UiKitTabsDemo.cs", "UiKitNavigationDemo.cs"])
            .Notes("The megamenu's panels are [popover] elements named by their triggers. Only two of the tab "
                + "rows hold their selected tab in the page; the rest keep track themselves.")
            .Result(UiKitNavigationDemo)
    ];
}
