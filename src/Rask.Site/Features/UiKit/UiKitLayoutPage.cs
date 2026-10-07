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
            "Flyout navigation, layout and mockups in C# — Rask",
            "Layout components in C#: a flyout panel whose open state the page owns, separator, join, "
            + "indicator, avatar, mask, and code, browser and window mockups.",
            Routes.UiKitLayoutPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Layout & mockups"],
        P.Class("text-ui-muted")[
            "A panel that slides in from an edge is a flyout: ", Code["Ui.Modal.Flyout().Left"],
            ", Flux UI's modal anchored to a side of the viewport. The page owns whether it is open — ",
            Code["Open"], " sets it and ", Code["OnClose"], " reports the reader closing it, by the corner ",
            "button, Escape or a click outside — which is what lets a page close it when a navigation completes."

        ],
        CodeSample
            .Files(["UiKitLayoutDemo.cs"])
            .Notes("The mockups are frames with no state. The code block is the only one carrying text, "
                + "and it encodes it: a snippet containing markup has to read as that markup rather "
                + "than becoming it.")
            .Result(UiKitLayoutDemo)
    ];
}
