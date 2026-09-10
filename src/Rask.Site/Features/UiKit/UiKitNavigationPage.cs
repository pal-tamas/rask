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
            "UI kit — Navigation — Rask",
            "daisyUI's Navigation components as typed Rask components: megamenu, tabs, menu, steps, "
            + "breadcrumbs, pagination, dock and link — built on native popovers and real URLs.",
            Routes.UiKitNavigationPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Navigation"],
        P.Class("text-ui-muted")[
            "This category is the browser's, deliberately. The megamenu is built on the native popover ",
            "API, so it gets the top layer, Escape and light-dismiss without a line of script; the tabs ",
            "are real links, so they are bookmarkable, survive a refresh and answer the back button. ",
            "Navigation is the first thing a reader touches and the last thing that should wait for a ",
            "bundle to boot."
        ],
        CodeSample
            .Files(["UiKitNavigationDemo.cs"])
            .Notes("Nothing on this page holds state in C#. The megamenu's panels are [popover] elements "
                + "named by their triggers, and every tab is an <a href>.")
            .Result(UiKitNavigationDemo)
    ];
}
