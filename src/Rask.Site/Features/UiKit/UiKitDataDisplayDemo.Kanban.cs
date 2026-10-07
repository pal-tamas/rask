namespace Rask.Site.Features.UiKit;

// Flux UI's kanban page, example by example: https://fluxui.dev/components/kanban
public sealed partial class UiKitDataDisplayDemo
{
    private const string Policy = "Update privacy policy in app";

    private readonly List<string> _planned = ["Search bar suggestions broken", "Improve loading spinner visuals"];
    private readonly NewCard _newCard = new();
    private string? _openedCard;

    private Component KanbanSection() =>
        Section(
            "Kanban",
            "Flux UI's kanban, example for example: cards in columns, one column per stage. It draws the board and "
            + "moves nothing, as Flux's does — a card is something to read until As makes it a button, and adding "
            + "or moving cards is this page changing its own lists.",
            Div.Data(Testid("ui-kanban")).Class("grid gap-6")[
                Div.Key("board").Class("overflow-x-auto pb-2")[Board()],
                Div.Key("columns").Class("flex flex-wrap items-start gap-6")[
                    WithActions(),
                    WithSubheading(),
                    WithFooter(),
                    Pressable()
                ],
                P.Key("opened").Class("text-sm text-ui-muted").Data(Testid("ui-kanban-opened"))[
                    _openedCard is null ? "No card opened yet." : $"Opened: {_openedCard}"
                ]
            ]);

    private static Component Board() =>
        Ui.Kanban.Data(Testid("ui-kanban-board"))[
            Stage("Planned", Policy, "Search bar suggestions broken", "Improve loading spinner visuals", "Date picker keyboard input fails"),
            Stage("In Progress", "Mobile responsive improvements", "Data table sorting broken", "API error codes inconsistent"),
            Stage("In review", "Button double-click issue", "Crash on large file upload")
        ];

    private static Component Stage(string title, params string[] cards) =>
        Ui.KanbanColumn.Key(title)[
            Ui.KanbanColumnHeader.Heading(title).Count(cards.Length),
            Ui.KanbanColumnCards[cards.Select(card => Ui.KanbanCard.Key(card).Heading(card))]
        ];

    private Component WithActions() =>
        Ui.KanbanColumn.Key("actions").Data(Testid("ui-kanban-actions"))[
            Ui.KanbanColumnHeader.Heading("Planned").Count(_planned.Count).Actions([
                Ui.Button.Subtle.Sm.Icon(Ui.IconName.EllipsisHorizontal).AriaLabel("Column options"),
                Ui.Button.Subtle.Sm.Icon(Ui.IconName.Plus).AriaLabel("New card").OnClick(() => _planned.Add($"Card {_planned.Count + 1}"))
            ]),
            Ui.KanbanColumnCards[_planned.Select(card => Ui.KanbanCard.Key(card).Heading(card))]
        ];

    private static Component WithSubheading() =>
        Ui.KanbanColumn.Key("subheading")[
            Ui.KanbanColumnHeader.Heading("Backlog").Subheading("Ideas and suggestions"),
            Ui.KanbanColumnCards[
                Ui.KanbanCard.Heading("Dark mode for the invoice PDF"),
                Ui.KanbanCard.Heading("Keyboard shortcuts cheat sheet")
            ]
        ];

    private Component WithFooter() =>
        Ui.KanbanColumn.Key("footer").Data(Testid("ui-kanban-footer"))[
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
                Ui.Button.Subtle.Sm.Icon(Ui.IconName.Plus).Align(Ui.Align.Start).OnClick(() => _planned.Add($"Card {_planned.Count + 1}"))["New card"]
            ]
        ];

    private Component Pressable() =>
        Ui.KanbanColumn.Key("buttons").Data(Testid("ui-kanban-buttons"))[
            Ui.KanbanColumnHeader.Heading("Planned"),
            Ui.KanbanColumnCards[
                Open("plain", Policy),
                Open("header", "Search bar suggestions broken").Header(
                    Div.Class("flex gap-2")[
                        Ui.Badge.Sm.Color(Ui.Color.Blue)["UI"],
                        Ui.Badge.Sm.Color(Ui.Color.Green)["Backend"],
                        Ui.Badge.Sm.Color(Ui.Color.Red)["Bug"]
                    ]),
                Open("footer", "Improve loading spinner visuals").Footer([
                    Ui.Icon.Name(Ui.IconName.Bars3BottomLeft).Variant(Ui.IconVariant.Micro).Class("text-zinc-400"),
                    Ui.Text.Inline()["3 people"]
                ])
            ]
        ];

    private UiKanbanCard Open(string key, string title) =>
        Ui.KanbanCard.Key(key).As(Ui.KanbanCardAs.Button).Heading(title).OnClick(() => { _openedCard = title; });

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
