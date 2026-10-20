namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class KanbanExample : Component
{
    private static readonly (string Stage, string[] Cards)[] Board =
    [
        ("Planned", ["Update privacy policy in app", "Search bar suggestions broken", "Improve loading spinner visuals", "Date picker keyboard input fails"]),
        ("In Progress", ["Mobile responsive improvements", "Data table sorting broken", "API error codes inconsistent"]),
        ("In review", ["Button double-click issue", "Crash on large file upload"]),
    ];

    // A column does not shrink, so a board wider than its place scrolls in a box of its own.
    protected override Component? Render() =>
        Div.Class("overflow-x-auto pb-2")[
            Ui.Kanban[
                Board.Select(column => Ui.KanbanColumn.Key(column.Stage)[
                    Ui.KanbanColumnHeader.Heading(column.Stage).Count(column.Cards.Length),
                    Ui.KanbanColumnCards[column.Cards.Select(card => Ui.KanbanCard.Key(card).Heading(card))]
                ])
            ]
        ];
}
