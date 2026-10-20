namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class KanbanCardButtonsExample : Component
{
    private string? _opened;

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.KanbanColumn[
                Ui.KanbanColumnHeader.Heading("Planned"),
                Ui.KanbanColumnCards[
                    Card("plain", "Update privacy policy in app"),
                    Card("header", "Search bar suggestions broken").Header(
                        Div.Class("flex gap-2")[
                            Ui.Badge.Sm.Color(Ui.Color.Blue)["UI"],
                            Ui.Badge.Sm.Color(Ui.Color.Green)["Backend"],
                            Ui.Badge.Sm.Color(Ui.Color.Red)["Bug"]
                        ]),
                    Card("footer", "Improve loading spinner visuals").Footer([
                        Ui.Icon.Name(Ui.IconName.Bars3BottomLeft).Variant(Ui.IconVariant.Micro).Class("text-zinc-400"),
                        Ui.Text.Inline()["3 people"]
                    ])
                ]
            ],
            Ui.Text.Data("testid", "ui-kanban-opened")[_opened is null ? "No card opened yet." : $"Opened: {_opened}"]
        ];

    private UiKanbanCard Card(string key, string title) =>
        Ui.KanbanCard.Key(key).As(Ui.KanbanCardAs.Button).Heading(title).OnClick(() => { _opened = title; });
}
