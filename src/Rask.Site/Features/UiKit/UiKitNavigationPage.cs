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
            + "pagination, with no state held in C#.",
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
            .Files(["UiKitNavigationDemo.cs"])
            .Notes("Nothing on this page has to hold state in C#: every tab, crumb and page number is an <a href>.")
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
