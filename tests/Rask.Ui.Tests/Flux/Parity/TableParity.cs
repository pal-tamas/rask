using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>Flux's <c>table</c> page: https://fluxui.dev/components/table.</summary>
/// <remarks>
///     <para>
///     A table's cells hold OTHER Flux components — avatars, badges, a row menu — and its page sets it in a
///     card and under a pager. None of those is rebuilt yet, so each is a plain box of the measured size
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
        + ".font-medium{font-weight:500}.mt-4{margin-top:16px}.mt-6{margin-top:24px}.max-h-80{max-height:320px}"
        + ".docs-text{color:#000}.dark .docs-text{color:#fff}"
        + ".bg-white{background-color:#fff}.dark .dark\\:bg-zinc-900{background-color:oklch(0.21 0.006 285.885)}";

    private const string Surface = "bg-white dark:bg-zinc-900";

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
        yield return ("", Frame(542, Intro(), leading: 24));
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
                    Ui.TableCell.Class("py-0")[RowMenu(32)]
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

    // flux:card, with its heading, text and button: stand-ins. The card states the gutter, as Flux's does.
    private static Component InACard() =>
        Div.Style("position:relative;padding:24px;border:1px solid rgb(0 0 0/.1);border-radius:12px;--ui-bleed:1.5rem")
            .Attributes(("data-ui-card", ""), ("data-parity-skip", "self"))[
            Div.Style("height:44px").Attributes(("data-parity-skip", ""))["Recent customers"],
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
                // flux:heading and flux:text: stand-ins.
                Div.Style("height:20px").Attributes(("data-ui-heading", ""), ("data-parity-skip", ""))["Recent customers"],
                P.Style("height:20px;margin-top:4px").Attributes(("data-ui-text", ""), ("data-parity-skip", ""))["Your latest customer activity."]
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
                    Ui.TableCell.Class("py-0")[RowMenu(38)]
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
                    Ui.TableCell.Class("py-0")[RowMenu(38)]
                ])
            ]
        ];

    // The width Flux's page gives the example, and the text it would inherit there.
    private static Component Frame(int width, Component example, int leading = 26) =>
        Div.Class("docs-text").Style($"width:{width}px;margin:0 auto;line-height:{leading}px")[Style[Raw.Value(AppUtilities)], example];

    // flux:avatar size="xs": a stand-in.
    private static Component Avatar() =>
        Div.Style("width:24px;height:24px;flex-shrink:0;border-radius:4px;background:#e4e4e7").Attributes(("data-parity-skip", ""));

    // flux:badge size="sm": a stand-in, set as Flux sets one so that its word gives it the measured width.
    private static Component Badge(string status) =>
        Div.Style("display:inline-flex;align-items:center;padding:4px 8px;border-radius:6px;font-size:12px;line-height:16px;font-weight:500;background:rgb(74 222 128/.2)")
            .Attributes(("data-parity-skip", ""))[status];

    // flux:dropdown around a ghost button: a stand-in. A glyph of text sets it on the line as Flux's sits.
    private static Component RowMenu(int width) =>
        Div.Style("display:inline-flex").Attributes(("data-parity-skip", ""))[
            Div.Style($"display:flex;align-items:center;justify-content:center;width:{width}px;height:32px;font-size:14px;line-height:20px")["…"]
        ];

    // flux:pagination: a stand-in. The table places it; the pager draws itself.
    private static Component Pager() =>
        Div.Style("display:flex;flex-shrink:0;align-items:center;justify-content:space-between;height:41px;padding-top:12px;border-top:1px solid #f4f4f5;font-size:12px")
            .Attributes(("data-ui-pagination", ""), ("data-parity-skip", ""))["Showing 1 to 5 of 24 results"];

    private sealed record Order(string Customer, string Date, string Status, string Amount, int Id = 0, string Email = "");
}
