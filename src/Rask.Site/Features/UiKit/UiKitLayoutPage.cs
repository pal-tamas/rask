using Rask.Core.Routing;
using Rask.Site.Features.UiDocs;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's Layout components, live.
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
            + "flyout panel and an avatar.",
            Routes.UiKitLayoutPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Layout & flyouts"],
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
        UiKitMovedPages.Pages([UiDocsCatalog.Separator, UiDocsCatalog.Heading, UiDocsCatalog.Text]),
        CodeSample
            .Files(["UiKitLayoutDemo.cs"])
            .Result(UiKitLayoutDemo)
    ];
}
