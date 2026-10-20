namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class KanbanFooterExample : Component
{
    private readonly List<string> _planned = ["Search bar suggestions broken", "Improve loading spinner visuals"];
    private readonly NewCard _newCard = new();

    protected override Component? Render() =>
        Ui.KanbanColumn[
            Ui.KanbanColumnHeader.Heading("Planned").Count(_planned.Count),
            Ui.KanbanColumnCards[_planned.Select(card => Ui.KanbanCard.Key(card).Heading(card))],
            Ui.KanbanColumnFooter[
                Form.Model(_newCard).OnSubmit(Add)[
                    Ui.KanbanCard[
                        Div.Class("flex items-center gap-1")[
                            Ui.Heading.Class("flex-1")[
                                Input.Bind(() => _newCard.Title).Class("w-full outline-none").Placeholder("New card...").AriaLabel("New card")
                            ],
                            Ui.Button.Filled.Sm.Type(Ui.ButtonType.Submit).Inset(Ui.Inset.Top | Ui.Inset.Bottom).Class("-me-1.5")["Add"]
                        ]
                    ]
                ],
                Ui.Button.Subtle.Sm.Icon(Ui.IconName.Plus).Align(Ui.Align.Start)
                    .OnClick(() => _planned.Add($"Card {_planned.Count + 1}"))["New card"]
            ]
        ];

    private void Add(NewCard card)
    {
        if (string.IsNullOrWhiteSpace(card.Title))
        {
            return;
        }

        _planned.Add(card.Title.Trim());
        card.Title = "";
    }

    private sealed class NewCard
    {
        public string Title { get; set; } = "";
    }
}
