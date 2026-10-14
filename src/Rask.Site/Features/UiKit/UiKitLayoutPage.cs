using Rask.Core.Routing;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's Layout components and the code mockup, live.
/// </summary>
[Route("ui/layout")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitLayoutPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets =>
        PageMeta.For(
            "Header and sidebar layouts and flyouts in C# — Rask",
            "Flux UI's header and sidebar layouts in C#, with a collapsible sidebar that needs no script, plus a "
            + "flyout panel, separator and a code mockup.",
            Routes.UiKitLayoutPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Layout & mockups"],
        P.Class("text-ui-muted")[
            "The application layout is Flux UI's: ", Code["Ui.Header"], ", ", Code["Ui.Sidebar"], " and ",
            Code["Ui.Main"], " side by side, and whatever holds them is the grid. The sidebar's two states — slid ",
            "over the page on a phone, narrowed to a rail on a desktop — are checkboxes, so they work before ",
            "anything has loaded; the runtime puts the sidebar away when a navigation completes and remembers ",
            "the rail across visits, as Flux's script does.",
            " A panel that slides in from an edge is a flyout: ", Code["Ui.Modal.Flyout().Left"],
            ", Flux UI's modal anchored to a side of the viewport, whose ", Code["Open"], " the page owns and whose ",
            Code["OnClose"], " reports the reader closing it."
        ],
        CodeSample
            .Files(["UiKitLayoutDemo.cs"])
            .Notes("The code mockup is a frame with no state, and it encodes its text: a snippet containing "
                + "markup has to read as that markup rather than becoming it.")
            .Result(UiKitLayoutDemo)
    ];
}
