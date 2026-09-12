using Rask.Core.Routing;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's tree, live.
/// </summary>
[Route("ui/tree")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitTreePage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets =>
        PageMeta.For(
            "A C# tree view: keyboard and virtualization — Rask",
            "UiTree in C#: expand and collapse, single or multiple selection, a keyboard with type-ahead, hover, "
            + "and virtualization.",
            Routes.UiKitTreePage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Tree"],
        P.Class("text-ui-muted")[
            "A hierarchy a reader can open, walk with the keyboard and select from. The nodes are the app's own ",
            "objects: the tree asks what identifies one, what it looks like, and what lies below it. Expansion and ",
            "selection are the page's or the tree's, one axis at a time — and a tree with thousands of nodes renders ",
            "only the rows on screen."
        ],
        CodeSample
            .Files(["UiKitTreeDemo.cs"])
            .Notes("One focusable element per tree: arrows move a cursor inside it, Right and Left open and climb, "
                + "typing jumps, and Enter selects. None of that is JavaScript — the kit ships none.")
            .Result(UiKitTreeDemo)
    ];
}
