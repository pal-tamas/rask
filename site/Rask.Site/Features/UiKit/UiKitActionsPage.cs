using Rask.Core.Routing;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's Actions components, live.
/// </summary>
/// <remarks>
///     One page per daisyUI category. The prose guide at <c>/guides/ui-kit</c> says what the kit is; this
///     says what it looks like and lets you press it, which is the part a paragraph cannot do.
/// </remarks>
[Route("ui/actions")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitActionsPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets =>
        PageMeta.For(
            "UI kit — Actions — Rask",
            "daisyUI's Actions components as typed Rask components: button, dropdown, modal, swap, "
            + "theme controller and the floating action button, each driven by C# state rather than a "
            + "CSS trick.",
            Routes.UiKitActionsPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Actions"],
        P.Class("text-ui-muted")[
            "Every daisyUI class these components can write is a complete literal in ", Code["UiClassNames"],
            " — daisyUI emits a component's CSS only where Tailwind can see the name, so a class built by ",
            "concatenation renders unstyled with a green build. The colour, fill and size axes compose."
        ],
        CodeSample
            .Files(["UiKitActionsDemo.cs"])
            .Notes("The dropdown's open state, the dialog's, and the swap's face are plain fields on the "
                + "demo component, changed in a callback and redrawn by the live diff. Nothing here is a "
                + "checkbox or a <details>.")
            .Result(UiKitActionsDemo)
    ];
}
