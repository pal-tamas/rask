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
            "Navigation components in C#: tabs that are real links, a nav list, steps, breadcrumbs and "
            + "Flux UI's pagination.",
            Routes.UiKitNavigationPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Navigation"],
        P.Class("text-ui-muted")[
            "This category is the browser's, deliberately. The tabs are real links, so they are ",
            "bookmarkable, survive a refresh and answer the back button. Navigation is the first thing a ",
            "reader touches and the last thing that should wait for a bundle to boot. A menu of links that ",
            "opens from a button is Ui.Navmenu in a Ui.Dropdown, on the Actions page."
        ],
        CodeSample
            .Files(["UiKitNavigationDemo.cs", "UiKitPaginationDemo.cs"])
            .Notes("Nothing on this page has to hold state in C#: every tab and crumb is an <a href>. The pagers "
                + "are the one exception: each keeps the page it is on in a field.")
            .Result(UiKitNavigationDemo)
    ];
}
