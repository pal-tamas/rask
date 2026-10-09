using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>Flux's <c>table</c> page: https://fluxui.dev/components/table.</summary>
/// <remarks>
///     <para>
///     A table's cells hold OTHER Flux components — avatars, badges, a row menu — and its page sets it in a
///     card and under a pager. The card, its heading and text, the pager, every button and the avatars are the
///     real ones. The dropdown around a row's button is not rebuilt yet, so it is a plain box
///     marked <c>data-parity-skip</c>: the comparison holds it to the room it takes and looks no further.
///     Everything that is the table's own is compared whole.
///     </para>
///     <para>
///     The live examples are fuller than the snippets printed beside them (the sticky tables have ten rows,
///     the bleeding ones draw their status as a badge); these follow what is rendered.
///     </para>
/// </remarks>
public sealed partial class TableParity : FluxParity
{
    // The utilities Flux's examples write on the table's parts, as an app's own stylesheet would hold them.
    // The kit's sheet is compiled from the kit's sources alone, so it has no reason to carry these.
    private const string AppUtilities =
        ".flex{display:flex}.items-center{align-items:center}.gap-3{gap:12px}.py-0{padding-block:0}"
        + ".font-medium{font-weight:500}.mt-1{margin-top:4px}.mt-4{margin-top:16px}.mt-6{margin-top:24px}.max-h-80{max-height:320px}.parity-menu-gap{margin-right:6px}"
        + ".parity-surface{background-color:#fff}.dark .parity-surface{background-color:oklch(0.21 0.006 285.885)}";

    // `bg-white dark:bg-zinc-900` there, under a name of its own: unlayered here, a real `.bg-white` would
    // beat the layered `dark:` of every kit component on the page that also writes one (the card).
    private const string Surface = "parity-surface";

    private static readonly Order[] Recent =
    [
        new("Gustavo Mango", "Jul 31, 9:50 AM", "Paid", "$162.00"),
        new("Desirae George", "Jul 31, 12:08 PM", "Paid", "$32.00"),
        new("Emery Madsen", "Jul 31, 11:50 AM", "Paid", "$163.00"),
        new("Kaylynn Schleifer", "Jul 31, 11:15 AM", "Incomplete", "$29.00"),
        new("Kaiya Bator", "Jul 31, 11:08 AM", "Failed", "$72.00"),
    ];

    private static readonly Order[] All =
    [
        new("Lindsey Aminoff", "Jul 29, 10:45 AM", "Paid", "$49.00", 428, "lindsey"),
        new("Hanna Lubin", "Jul 28, 2:15 PM", "Paid", "$312.00", 427, "hanna"),
        new("Kianna Bushevi", "Jul 30, 4:05 PM", "Paid", "$132.00", 426, "kianna"),
        new("Gustavo Geidt", "Jul 27, 9:30 AM", "Paid", "$31.00", 424, "gustavo"),
        new("Nolan George", "Jul 26, 7:55 PM", "Paid", "$313.00", 423, "nolan"),
        new("Desirae George", "Jul 31, 12:08 PM", "Paid", "$32.00", 421, "desirae"),
        new("Jackson Bothman", "Jul 30, 3:45 PM", "Refunded", "$94.00", 420, "jackson"),
        new("Hanna Lipshutz", "Jul 29, 11:30 AM", "Paid", "$22.00", 419, "lipshutz"),
        new("Alfredo Levin", "Jul 25, 10:10 AM", "Paid", "$12.00", 418, "alfredo"),
        new("Zain Lubin", "Jul 28, 9:20 AM", "Paid", "$91.00", 417, "zain"),
    ];

    public override string Page => "table";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Frame(542, Intro()));
        yield return ("simple", Frame(414, Simple()));
        yield return ("full-bleed", Frame(542, InACard()));
        yield return ("full-bleed", Frame(414, InABoxOfItsOwn()));
        yield return ("pagination", Frame(414, Ui.Table.Paginate(Pager())));
        yield return ("sortable", Frame(414, Sortable()));
        yield return ("sticky-header", Frame(542, StickyHeader()));
        yield return ("sticky-columns", Frame(542, StickyColumns()));
    }

    // Flux sorts with wire:click on the column; here that is OnSort, which draws nothing.
    private static Component Intro() =>
        Ui.Table.Paginate(Pager())[
            Ui.TableColumns[
                Ui.TableColumn["Customer"],
                Ui.TableColumn.Sortable().Sorted().Direction(Ui.TableColumnDirection.Desc)["Date"],
                Ui.TableColumn.Sortable()["Status"],
                Ui.TableColumn.Sortable()["Amount"],
                Ui.TableColumn
            ],
            Ui.TableRows[
                Recent.Select(order => Ui.TableRow.Key(order.Customer)[
                    Ui.TableCell.Class("flex items-center gap-3")[Avatar(), order.Customer],
                    Ui.TableCell.Class("whitespace-nowrap")[order.Date],
                    Ui.TableCell.Class("py-0")[Badge(order.Status)],
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[order.Amount],
                    Ui.TableCell.Class("py-0")[RowMenu()]
                ])
            ]
        ];

    private static Component Simple() =>
        Ui.Table[
            Ui.TableColumns[
                Ui.TableColumn["Customer"],
                Ui.TableColumn["Date"],
                Ui.TableColumn["Status"],
                Ui.TableColumn["Amount"]
            ],
            Ui.TableRows[
                SimpleRow("Lindsey Aminoff", "Jul 29, 10:45 AM", "Paid", "$49.00"),
                SimpleRow("Hanna Lubin", "Jul 28, 2:15 PM", "Paid", "$312.00"),
                SimpleRow("Kianna Bushevi", "Jul 30, 4:05 PM", "Refunded", "$132.00"),
                SimpleRow("Gustavo Geidt", "Jul 27, 9:30 AM", "Paid", "$31.00")
            ]
        ];

    private static Component SimpleRow(string customer, string date, string status, string amount) =>
        Ui.TableRow[
            Ui.TableCell[customer],
            Ui.TableCell[date],
            Ui.TableCell.Class("py-0")[Badge(status)],
            Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[amount]
        ];

    // The card is Ui.Card and states the gutter; its heading row is the app's own flex row.
    private static Component InACard() =>
        Ui.Card[
            Div.Style("display:flex;align-items:center;justify-content:space-between;gap:16px")[
                Div[Ui.Heading["Recent customers"], Ui.Text.Class("mt-1")["Your latest customer activity."]],
                Ui.Button.Sm.Icon(Ui.IconName.Plus)["Add customer"]
            ],
            Ui.Table.Bleed().ContainerClass("mt-6")[
                Ui.TableColumns[
                    Ui.TableColumn["Customer"],
                    Ui.TableColumn["Date"],
                    Ui.TableColumn["Status"],
                    Ui.TableColumn.End["Amount"]
                ],
                Ui.TableRows[
                    Recent.Take(4).Select(order => Ui.TableRow.Key(order.Customer)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[order.Customer],
                        Ui.TableCell[order.Date],
                        Ui.TableCell.Class("py-0")[Badge(order.Status)],
                        Ui.TableCell.End.Variant(Ui.TableCellVariant.Strong)[order.Amount]
                    ])
                ]
            ]
        ];

    // "Custom gutters": a padded box that is not a card says how far the table may bleed.
    private static Component InABoxOfItsOwn() =>
        Div.Style("padding:16px;border:1px solid #e4e4e7;border-radius:8px;--ui-bleed:1rem")[
            Div[
                Ui.Heading["Recent customers"],
                Ui.Text.Class("mt-1")["Your latest customer activity."]
            ],
            Ui.Table.Bleed().ContainerClass("mt-4")[
                Ui.TableColumns[
                    Ui.TableColumn["Customer"],
                    Ui.TableColumn["Status"],
                    Ui.TableColumn.End["Amount"]
                ],
                Ui.TableRows[
                    Recent.Take(3).Select(order => Ui.TableRow.Key(order.Customer)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[order.Customer],
                        Ui.TableCell.Class("py-0")[Badge(order.Status)],
                        Ui.TableCell.End.Variant(Ui.TableCellVariant.Strong)[order.Amount]
                    ])
                ]
            ]
        ];

    private static Component Sortable() =>
        Ui.Table[
            Ui.TableColumns[
                Ui.TableColumn["Customer"],
                Ui.TableColumn.Sortable().Sorted().Direction(Ui.TableColumnDirection.Desc)["Date"],
                Ui.TableColumn.Sortable()["Amount"]
            ],
            Ui.TableRows[
                Recent.Take(4).Select(order => Ui.TableRow.Key(order.Customer)[
                    Ui.TableCell.Class("flex items-center gap-3")[Avatar(), order.Customer],
                    Ui.TableCell[order.Date],
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[order.Amount]
                ])
            ]
        ];

    private static Component StickyHeader() =>
        Ui.Table.ContainerClass("max-h-80")[
            Ui.TableColumns.Sticky().Class(Surface)[
                Ui.TableColumn["Customer"],
                Ui.TableColumn["Date"],
                Ui.TableColumn["Status"],
                Ui.TableColumn["Amount"],
                Ui.TableColumn
            ],
            Ui.TableRows[
                All.Select(order => Ui.TableRow.Key(order.Id)[
                    Ui.TableCell.Class("flex items-center gap-3")[Avatar(), order.Customer],
                    Ui.TableCell[order.Date],
                    Ui.TableCell.Class("py-0")[Badge(order.Status)],
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[order.Amount],
                    Ui.TableCell.Class("py-0")[RowMenu("parity-menu-gap")]
                ])
            ]
        ];

    private static Component StickyColumns() =>
        Ui.Table.ContainerClass("max-h-80")[
            Ui.TableColumns.Sticky().Class(Surface)[
                Ui.TableColumn.Sticky().Class(Surface)["ID"],
                Ui.TableColumn.Class(Surface)["Customer"],
                Ui.TableColumn["Email"],
                Ui.TableColumn["Date"],
                Ui.TableColumn["Status"],
                Ui.TableColumn["Amount"],
                Ui.TableColumn
            ],
            Ui.TableRows[
                All.Select(order => Ui.TableRow.Key(order.Id)[
                    Ui.TableCell.Sticky().Class("font-medium " + Surface)[Span.Style("opacity:.5")["#"], order.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                    Ui.TableCell.Class("flex items-center gap-3")[Avatar(), order.Customer],
                    Ui.TableCell[order.Email + "@example.com"],
                    Ui.TableCell[order.Date],
                    Ui.TableCell.Class("py-0")[Badge(order.Status)],
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[order.Amount],
                    Ui.TableCell.Class("py-0")[RowMenu("parity-menu-gap")]
                ])
            ]
        ];

    // The width Flux's page gives the example.
    private static Component Frame(int width, Component example) =>
        Div.Style($"width:{width}px;margin:0 auto")[Style[Raw.Value(AppUtilities)], example];

    // <flux:avatar size="xs" src="…">: whose face it is comes from the docs page's own data.
    private static Component Avatar() => Ui.Avatar.Xs.Src(NavigationStandIns.Caleb);

    // flux:badge size="sm", in the colour Flux's page gives each status.
    private static Component Badge(string status) =>
        Ui.Badge.Sm.Color(status switch
        {
            "Paid" => Ui.Color.Green,
            "Failed" => Ui.Color.Red,
            "Incomplete" => Ui.Color.Amber,
            _ => null,
        })[status];

    // flux:dropdown around a ghost button. The dropdown and its closed menu are stand-ins — neither is
    // rebuilt yet — and the button between them is compared. The sticky tables' examples set theirs 6px in
    // from the edge, which is the docs page's doing and is stated in AppUtilities.
    private static Component RowMenu(string? gap = null) =>
        Div.Style("display:inline-flex").Attributes(("data-ui-dropdown", ""), ("data-parity-skip", "self"))[
            Ui.Button.Ghost.Sm.Icon(Ui.IconName.EllipsisHorizontal).Class(gap),
            Div.Style("display:none").Attributes(("data-ui-menu", ""), ("data-parity-skip", ""))
        ];

    // flux:pagination, as the docs page's table is handed it: 24 orders, five to a page.
    private static UiPagination Pager() =>
        Ui.Pagination.Paginator(new UiPaginator { Page = 1, PerPage = 5, Total = 24 });

    private sealed record Order(string Customer, string Date, string Status, string Amount, int Id = 0, string Email = "");
}
