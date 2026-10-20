using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's kanban and its parts as a C# component: each example with its own code, and its reference.</summary>
[Route("kanban")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiKanbanPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Kanban.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Kanban)
            .Lead("Cards arranged in columns, one column per stage of a workflow. It draws the board and moves "
                  + "nothing, as Flux's does: adding or moving a card is the page changing its own lists.")
            .Api(UiApi.Kanban)[
            UiDocsExample.Key("ui-kanban").Heading("Kanban").File("KanbanExample.cs").Demo(KanbanExample).Testid("ui-kanban")
                .Blurb("A column per stage, each with its name, a count and its cards."),
            UiDocsExample.Key("ui-kanban-actions").Heading("Column actions").File("KanbanActionsExample.cs").Demo(KanbanActionsExample).Testid("ui-kanban-actions")
                .Blurb("Buttons at the end of the heading's row. The plus adds a card to the page's list."),
            UiDocsExample.Key("ui-kanban-subheading").Heading("Subheading").File("KanbanSubheadingExample.cs").Demo(KanbanSubheadingExample).Testid("ui-kanban-subheading"),
            UiDocsExample.Key("ui-kanban-footer").Heading("Column footer").File("KanbanFooterExample.cs").Demo(KanbanFooterExample).Testid("ui-kanban-footer")
                .Blurb("A form under the cards that adds one, and the count follows."),
            UiDocsExample.Key("ui-kanban-buttons").Heading("Cards as buttons").File("KanbanCardButtonsExample.cs").Demo(KanbanCardButtonsExample).Testid("ui-kanban-buttons")
                .Blurb("As(Ui.KanbanCardAs.Button) makes the whole card one to press, with badges above its heading "
                       + "or an icon under it."),
            UiKanbanBehaviour
        ];
}
