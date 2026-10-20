namespace Rask.Site.Features.UiDocs;

/// <summary>How a skeleton behaves: its sizes, how it animates and with what light, and what it says.</summary>
internal sealed partial class UiSkeletonBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Shapes(),
            Animation(),
            Announced()
        ];

    private static UiDocsTopic Shapes() =>
        UiDocsTopic.Key("shapes").Heading("Sizes").Paragraphs([
            "`Ui.Skeleton` on its own is a 16px bar the width of its container; `.Class(\"size-10 rounded-full\")` "
            + "makes it an avatar, `.Class(\"aspect-[4/1] rounded-lg\")` a chart. Its height and radius are written at "
            + "zero specificity, so the call site's always win.",
            "`Ui.SkeletonLine` takes up the whole line height of the text it stands in for and draws a bar the height "
            + "of the letters, so a page keeps its rhythm when the words land: `Base`, a 14px bar in a 20px line, or "
            + "`Lg`, a 16px bar in a 24px line. Its width is the container's; `.Class(\"w-1/2\")` shortens it.",
        ]);

    private static UiDocsTopic Animation() =>
        UiDocsTopic.Key("animation").Heading("Animation").Paragraphs([
            "`Shimmer` carries a band of light across, left to right, every two seconds; `Pulse` fades to half and "
            + "back, every two seconds. A skeleton is still unless it, or a group around it, says otherwise.",
            "`Ui.SkeletonGroup` is a `<div>` that draws nothing. Its `Animate` reaches every skeleton and line inside "
            + "it, however deep, that does not state its own: `Ui.Skeleton.Pulse` or "
            + "`.Animate(Ui.SkeletonAnimate.None)` keeps its own.",
            "The shimmer's light is `--ui-shimmer-color`: white, and `zinc-900` in dark. On a surface that is neither, "
            + "set it to that surface's colour. As in Flux, neither animation stops under `prefers-reduced-motion`.",
        ]);

    private static UiDocsTopic Announced() =>
        UiDocsTopic.Key("announced").Heading("What it says").Paragraphs([
            "Nothing: a skeleton carries no ARIA of its own. Say that the region is loading on the region "
            + "(`aria-busy`). The roots are marked `data-ui-skeleton`, `data-ui-skeleton-line` and "
            + "`data-ui-skeleton-group`.",
        ]);
}
