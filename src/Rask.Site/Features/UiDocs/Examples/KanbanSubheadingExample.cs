namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class KanbanSubheadingExample : Component
{
    protected override Component? Render() =>
        Ui.KanbanColumn[
            Ui.KanbanColumnHeader.Heading("Backlog").Subheading("Ideas and suggestions"),
            Ui.KanbanColumnCards[
                Ui.KanbanCard.Heading("Dark mode for the invoice PDF"),
                Ui.KanbanCard.Heading("Keyboard shortcuts cheat sheet")
            ]
        ];
}
