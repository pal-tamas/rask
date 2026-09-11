using Rask.Core.Routing;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's Layout and Mockup components, live.
/// </summary>
/// <remarks>
///     One page for two daisyUI categories, because the mockups are four frames with no state and no
///     options between them — a page of their own would be a page with four pictures on it.
/// </remarks>
[Route("ui/layout")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitLayoutPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets =>
        PageMeta.For(
            "daisyUI drawer, layout and mockups in C# — Rask",
            "daisyUI layout components in C#: a drawer whose open state C# reads and sets, divider, join, "
            + "indicator, avatar, mask, and code, browser and window mockups.",
            Routes.UiKitLayoutPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Layout & mockups"],
        P.Class("text-ui-muted")[
            "The drawer is the one interactive component in the kit whose state stays in a checkbox. ",
            "That is not a leftover: daisyUI's rules are written against ", Code[".drawer-toggle:checked"],
            ", so the input is the component rather than an implementation detail. What C# gets is the ",
            "same state in both directions — ", Code["Open"], " sets it, ", Code["OnToggle"],
            " reports it — which is what lets a page close the drawer when a navigation completes."
        ],
        CodeSample
            .Files(["UiKitLayoutDemo.cs"])
            .Notes("The mockups are frames with no state. The code block is the only one carrying text, "
                + "and it encodes it: a snippet containing markup has to read as that markup rather "
                + "than becoming it.")
            .Result(UiKitLayoutDemo)
    ];
}
