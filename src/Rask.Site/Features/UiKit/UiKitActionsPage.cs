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
            "Buttons, modals and dropdowns as C# components — Rask",
            "Flux UI's button and button group as typed C# components, with a dropdown, modal, swap, theme "
            + "controller and floating action button driven from C# fields.",
            Routes.UiKitActionsPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Actions"],
        P.Class("text-ui-muted")[
            "The button and its group are Flux UI's, example for example: ", Code["Ui.Button.Primary.Sm.Blue"],
            " is a variant, a size and a colour, each a step. The rest of this page is still drawn with daisyUI ",
            "and moves over component by component."
        ],
        CodeSample
            .Files(["UiKitButtonDemo.cs", "UiKitActionsDemo.cs"])
            .Notes("The dropdown's open state, the dialog's, and the swap's face are plain fields on the "
                + "demo component, changed in a callback and redrawn by the live diff. Nothing here is a "
                + "checkbox or a <details>.")
            .Result(UiKitActionsDemo)
    ];
}
