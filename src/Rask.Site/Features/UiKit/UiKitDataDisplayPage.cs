using Rask.Core.Routing;
using Rask.Site.Features.UiDocs;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's Data display components, live.
/// </summary>
/// <remarks>
///     It also answers the data grid's and the tree's old pages: both left the kit, and the table here is what a
///     list of rows is drawn with now, so a link or a bookmark to either lands on it rather than on a 404.
/// </remarks>
[Route("ui/data-display")]
[Route("ui/data-grid")]
[Route("ui/tree")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitDataDisplayPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets =>
        PageMeta.For(
            "Accordion, table and chart components in C# — Rask",
            "Data display components in C#: Flux UI's accordion on native details, kanban, sortable table, chart, "
            + "timeline, carousel and flag.",
            Routes.UiKitDataDisplayPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Data display"],
        P.Class("text-ui-muted")[
            "The kanban is Flux's: ", Code["Ui.Kanban"], " holds ", Code["Ui.KanbanColumn"], "s of ",
            Code["Ui.KanbanCard"], "s, and a card that opens is ", Code[".As(Ui.KanbanCardAs.Button).OnClick(…)"], ". ",
            "The rest of this category is mostly static. The accordion is Flux's: every item is a ", Code["<details>"],
            ", so it opens and closes in the browser with no handler, and ", Code["Exclusive()"], " is the ",
            "platform's own ", Code["name"], " group. A page that wants to own an item gives it ",
            Code["Expanded"], " and listens to ", Code["OnToggle"], "."
        ],
        UiKitMovedPages.Pages([UiDocsCatalog.Badge, UiDocsCatalog.Card]),
        CodeSample
            .Files(["UiKitDataDisplayDemo.cs", "UiKitDataDisplayDemo.Kanban.cs", "UiKitDataDisplayDemo.Table.cs", "UiKitDataDisplayDemo.StableColumns.cs", "UiKitDataDisplayDemo.PagingThatWaits.cs", "UiKitTimelineDemo.cs", "UiKitCarouselDemo.cs", "UiKitFlagDemo.cs"])
            .Notes("Only the accordion the page owns keeps a field; the rest hold no state. "
                + "The table is Flux's: the page keeps the sorted column "
                + "and the page number, and says so with Sorted, Direction and Paginate. Its columns stay put "
                + "across pages once the table has a width (w-full) and each column but one has its own. "
                + "A page change shows nothing until its rows are back; a slow one dims the rows it replaces, "
                + "with no flag in the page.")
            .Result(UiKitDataDisplayDemo)
    ];
}
