namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class KanbanActionsExample : Component
{
    private readonly List<string> _planned = ["Search bar suggestions broken", "Improve loading spinner visuals"];

    protected override Component? Render() =>
        Ui.KanbanColumn[
            Ui.KanbanColumnHeader.Heading("Planned").Count(_planned.Count).Actions([
                Ui.Button.Subtle.Sm.Icon(Ui.IconName.EllipsisHorizontal).AriaLabel("Column options"),
                Ui.Button.Subtle.Sm.Icon(Ui.IconName.Plus).AriaLabel("New card").OnClick(() => _planned.Add($"Card {_planned.Count + 1}"))
            ]),
            Ui.KanbanColumnCards[_planned.Select(card => Ui.KanbanCard.Key(card).Heading(card))]
        ];
}
