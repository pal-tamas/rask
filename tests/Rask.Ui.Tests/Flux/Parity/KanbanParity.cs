using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>fluxui.dev/components/kanban, example by example.</summary>
/// <remarks>
///     <para>
///     The examples follow what the page RENDERS, which is not always the snippet printed beside it: the
///     footer example's header is given <c>count="5"</c> in the snippet and draws no count, and every column
///     but the first board's is filled with skeletons rather than cards.
///     </para>
///     <para>
///     The buttons, badges, icons, headings, skeletons and avatars are the real ones. The dropdown is still a
///     marked wrapper around the real button with its hidden menu as a box.
///     </para>
/// </remarks>
public sealed partial class KanbanParity : FluxParity
{
    // What Flux's examples hand to `class`, as an app's own stylesheet would hold it.
    private const string AppUtilities =
        "<style>.parity-card{height:40px;border-radius:8px;opacity:.75}.parity-grow{flex:1 1 0%}"
        + ".parity-row{display:flex;align-items:center;gap:4px}.parity-input{width:100%;outline-style:none}"
        + ".parity-pull{margin-inline-end:-6px}.parity-tags{display:flex;gap:8px}"
        + ".parity-zinc-400{color:var(--color-zinc-400)}"
        + ".parity-card-ring>*:where(.dark,.dark *){--tw-ring-color:oklch(37% .013 285.805)}"
        // `dark:ring-1 dark:ring-zinc-700`, which the page gives the footer example's placeholders only.
        + ".dark .parity-ringed{box-shadow:0 0 #0000,0 0 #0000,0 0 #0000,0 0 0 1px var(--color-zinc-700),0 0 #0000}</style>";

    private const string CardTitle = "Update privacy policy in app";

    public override string Page => "kanban";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Board());
        yield return ("column-actions", Alone(ColumnActions()));
        yield return ("column-subheading", Alone(ColumnSubheading()));
        yield return ("column-footer", Alone(ColumnFooter()));
        yield return ("card-as-button", Alone(Planned(Ui.KanbanCard.As(Ui.KanbanCardAs.Button).Heading(CardTitle))));
        yield return ("card-header", Alone(Planned(CardHeader())));
        yield return ("card-footer", Alone(Planned(CardFooter())));
    }

    // The page's first example sits in the hero, whose line is 24px, in a box of its own that scrolls
    // sideways. The 192px at the end is the page's, not the board's: room for the last column to scroll out
    // from under the fade the page draws over that edge.
    private static Component Board() =>
        Div.Style("width:606px;margin:0 auto;overflow:auto")[
            Ui.Kanban[
                Stage("Planned", "Update privacy policy in app", "Search bar suggestions broken", "Improve loading spinner visuals",
                    "Date picker keyboard input fails", "Admin panel permissions broken", "Broken image links in gallery"),
                Stage("In Progress", "Mobile responsive improvements", "Data table sorting broken", "API error codes inconsistent",
                    "Accessibility audit in progress", "User dashboard redesign"),
                Stage("In review", "Button double-click issue", "Crash on large file upload", "API concurrent request handling"),
                Div.Style("width:192px;height:24px;flex-shrink:0")
            ]
        ];

    private static Component Stage(string title, params string[] cards) =>
        Ui.KanbanColumn.Key(title)[
            Ui.KanbanColumnHeader.Heading(title).Count(cards.Length),
            Ui.KanbanColumnCards[cards.Select(card => Ui.KanbanCard.Key(card).Heading(card))]
        ];

    private static Component ColumnActions() =>
        Ui.KanbanColumn[
            Ui.KanbanColumnHeader.Heading("Planned").Count(4).Actions([
                Div.Style("display:flex").Attributes(("data-ui-dropdown", ""), ("data-parity-skip", "self"))[
                    Ui.Button.Subtle.Sm.Icon(Ui.IconName.EllipsisHorizontal),
                    Div.Style("display:none").Attributes(("data-ui-menu", ""), ("data-parity-skip", ""))
                ],
                Ui.Button.Subtle.Sm.Icon(Ui.IconName.Plus)
            ]),
            Ui.KanbanColumnCards[Placeholders(4)]
        ];

    private static Component ColumnSubheading() =>
        Ui.KanbanColumn[
            Ui.KanbanColumnHeader.Heading("Blacklog").Subheading("Ideas and suggestions"),
            Ui.KanbanColumnCards[Placeholders(4)]
        ];

    private static Component ColumnFooter() =>
        Ui.KanbanColumn[
            Ui.KanbanColumnHeader.Heading("Planned"),
            Ui.KanbanColumnCards[Placeholders(3, "parity-card parity-ringed")],
            Ui.KanbanColumnFooter[
                Form.Model(new object())[
                    Ui.KanbanCard[
                        Div.Class("parity-row")[
                            Ui.Heading.Class("parity-grow")[Input.Value("").Class("parity-input").Placeholder("New card...")],
                            Ui.Button.Filled.Sm.Type(Ui.ButtonType.Submit).Inset(Ui.Inset.Top | Ui.Inset.Bottom).Class("parity-pull")["Add"]
                        ]
                    ]
                ],
                Ui.Button.Subtle.Sm.Icon(Ui.IconName.Plus).Align(Ui.Align.Start)["New card"]
            ]
        ];

    private static Component CardHeader() =>
        Ui.KanbanCard.As(Ui.KanbanCardAs.Button).Heading(CardTitle).Header(
            Div.Class("parity-tags")[
                Ui.Badge.Sm.Color(Ui.Color.Blue)["UI"],
                Ui.Badge.Sm.Color(Ui.Color.Green)["Backend"],
                Ui.Badge.Sm.Color(Ui.Color.Red)["Bug"]
            ]);

    // <flux:avatar.group> of three faces and a count, each `circle size="xs"`.
    private static Component CardFooter() =>
        Ui.KanbanCard.As(Ui.KanbanCardAs.Button).Heading(CardTitle).Footer([
            Ui.Icon.Name(Ui.IconName.Bars3BottomLeft).Variant(Ui.IconVariant.Micro).Class("parity-zinc-400"),
            // In dark Flux rings these in the card's own ground (zinc-700), where a group's ring is zinc-900:
            // whose rule that is was not found, so the page states it, as an app on another ground would.
            Ui.AvatarGroup.Class("parity-card-ring")[
                Ui.Avatar.Circle().Xs.Src(NavigationStandIns.Caleb),
                Ui.Avatar.Circle().Xs.Src(NavigationStandIns.Hugo),
                Ui.Avatar.Circle().Xs.Src(NavigationStandIns.Josh),
                Ui.Avatar.Circle().Xs["3+"]
            ]
        ]);

    private static Component Planned(Component card) =>
        Ui.KanbanColumn[
            Ui.KanbanColumnHeader.Heading("Planned"),
            Ui.KanbanColumnCards[card]
        ];

    private static IEnumerable<Component> Placeholders(int count, string look = "parity-card") =>
        Enumerable.Range(0, count).Select(i => (Component)Ui.Skeleton.Key(i).Class(look));

    // A column by itself, centred in the 384px the page gives these examples.
    private static Component Alone(Component column) =>
        Div.Style("display:flex;justify-content:center;max-width:384px;margin:0 auto")[
            Raw.Value(AppUtilities),
            Div[column]
        ];
}
