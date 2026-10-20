using Rask.Core.Routing;
using Rask.Site.Features.UiDocs;

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
            "Flux UI's button and button group as typed C# components, with a dropdown, context menu, modal "
            + "and popover whose state is a plain C# field on the page.",
            Routes.UiKitActionsPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Actions"],
        P.Class("text-ui-muted")[
            "Menus, the command palette, popovers and modals, each Flux UI's and each opened by the browser: their state ",
            "is a field on the page when the page wants it. They move to the UI tab one page per component."
        ],
        UiKitMovedPages.Pages([UiDocsCatalog.Button]),
        CodeSample
            .Files(["UiKitActionsDemo.cs"])
            .Notes("The dropdown's open state and the dialog's are plain fields on the "
                + "demo component, changed in a callback and redrawn by the live diff. Nothing here is a "
                + "checkbox or a <details>.")
            .Result(UiKitActionsDemo)
    ];
}
