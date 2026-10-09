using Rask.Core.Routing;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's Data display components, live.
/// </summary>
[Route("ui/data-display")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitDataDisplayPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets =>
        PageMeta.For(
            "Badge, accordion and card components in C# — Rask",
            "Data display components in C#: a Flux UI badge, a Flux-style accordion on native details, card, kbd, "
            + "status, countdown, chat bubble, aura and hover effects.",
            Routes.UiKitDataDisplayPage());

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Data display"],
        P.Class("text-ui-muted")[
            "The badge is Flux UI's, example for example: ", Code["Ui.Badge.Color(Ui.Color.Lime)[\"New\"]"],
            ", pressed with ", Code[".As(Ui.BadgeAs.Button).OnClick(…)"], " and removable with a ",
            Code["Ui.BadgeClose"], " among its children. ",
            "The kanban is Flux's too: ", Code["Ui.Kanban"], " holds ", Code["Ui.KanbanColumn"], "s of ",
            Code["Ui.KanbanCard"], "s, and a card that opens is ", Code[".As(Ui.KanbanCardAs.Button).OnClick(…)"], ". ",
            "The rest of this category is mostly static. The accordion is Flux's: every item is a ", Code["<details>"],
            ", so it opens and closes in the browser with no handler, and ", Code["Exclusive()"], " is the ",
            "platform's own ", Code["name"], " group. A page that wants to own an item gives it ",
            Code["Expanded"], " and listens to ", Code["OnToggle"], "."
        ],
        CodeSample
            .Files(["UiKitDataDisplayDemo.cs", "UiKitDataDisplayDemo.Kanban.cs", "UiKitDataDisplayDemo.Table.cs", "UiKitDataDisplayDemo.StableColumns.cs", "UiKitTimelineDemo.cs"])
            .Notes("Only the accordion the page owns keeps a field; the rest hold no state. Aura, hover 3D "
                + "and hover gallery are decoration — they carry no role and no label, because a reader "
                + "who cannot see them loses nothing. The table is Flux's: the page keeps the sorted column "
                + "and the page number, and says so with Sorted, Direction and Paginate. Its columns stay put "
                + "across pages once the table has a width (w-full) and each column but one has its own.")
            .Result(UiKitDataDisplayDemo)
    ];
}
