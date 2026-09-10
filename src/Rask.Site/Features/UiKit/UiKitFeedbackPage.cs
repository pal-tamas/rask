using Rask.Core.Routing;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's Feedback components, live.
/// </summary>
[Route("ui/feedback")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitFeedbackPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets =>
        PageMeta.For(
            "UI kit — Feedback — Rask",
            "daisyUI's Feedback components as typed Rask components: alert, loading, progress, radial "
            + "progress, skeleton, toast and tooltip — each carrying its meaning in words, not only in "
            + "colour.",
            Routes.UiKitFeedbackPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Feedback"],
        P.Class("text-ui-muted")[
            "The recurring theme in this category is what gets ", Em["announced"], ". A spinner tells a ",
            "screen reader nothing, a colour is invisible to a reader who cannot distinguish it, and a ",
            "tooltip cannot be reached by touch at all — so the loading indicator is ",
            Code["aria-hidden"], " with its words beside it, the toast is a ", Code["role=\"status\""],
            " announced politely rather than interrupting, and a failed toast changes its ",
            Em["icon"], " and not only its colour."
        ],
        CodeSample
            .Files(["UiKitFeedbackDemo.cs"])
            .Notes("The progress value and the toast are fields on the demo component. Loading shapes "
                + "and tooltip placements are closed enums, because a misspelled class name is not a "
                + "compile error and a class daisyUI never defined styles nothing.")
            .Result(UiKitFeedbackDemo)
    ];
}
