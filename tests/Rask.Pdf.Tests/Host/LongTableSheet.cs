using System.Globalization;
using Rask.Core;

namespace Rask.Printing.Tests.Host;

// A 14-column refund report, as an app prints one: the measured page of PrintMeasurements.
public sealed partial class LongTableSheet : Component
{
    private const string SmallPrint =
        "@media print { body, table, td, th { font-size: 7pt !important; line-height: 1.2 !important; } "
        + "td, th { padding: 1px 3px !important; } }";

    private static readonly string[] Headings =
    [
        "Dátum", "Számlaszám", "Rendszám", "Kút", "Liter", "Nettó", "ÁFA", "Bruttó", "Visszatérítés", "Sofőr", "Kártya",
        "Termék", "Megjegyzés", "Státusz",
    ];

    public int? Rows { get; set; }

    public LongTableLook? Look { get; set; }

    protected override Component? Render() =>
        Div[
            Look is LongTableLook.KitSheet or LongTableLook.KitSheetSmall ? Style[Raw.Value(UiStylesheet.Css)] : null,
            Look is LongTableLook.KitSheetSmall ? Style[Raw.Value(SmallPrint)] : null,
            H1["Gázolaj-visszatérítés"],
            Look is null or LongTableLook.Plain ? PlainTable() : KitTable()
        ];

    private Component PlainTable() =>
        Table[
            Thead[Tr[Headings.Select((heading, column) => Th.Key(column)[heading])]],
            Tbody[Enumerable.Range(1, Rows ?? 1000).Select(row => Tr.Key(row)[Cells(row).Select((cell, column) => Td.Key(column)[cell])])]
        ];

    private Component KitTable() =>
        Ui.Table[
            Ui.TableColumns[Headings.Select((heading, column) => Ui.TableColumn.Key(column)[heading])],
            Ui.TableRows[Enumerable.Range(1, Rows ?? 1000).Select(row =>
                Ui.TableRow.Key(row)[Cells(row).Select((cell, column) => Ui.TableCell.Key(column)[cell])])]
        ];

    private static string[] Cells(int row) =>
    [
        new DateOnly(2026, 7, 1).AddDays(row % 92).ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
        string.Create(CultureInfo.InvariantCulture, $"SZ-{row:D5}"),
        string.Create(CultureInfo.InvariantCulture, $"ŐŰ-{row % 400:D3}"),
        string.Create(CultureInfo.InvariantCulture, $"Győr, Fehérvári út {row % 120}"),
        string.Create(CultureInfo.InvariantCulture, $"{row % 90 + 10},5"),
        Forints(row * 731 % 250000),
        Forints(row * 197 % 67000),
        Forints(row * 929 % 317000),
        Forints(row * 37 % 12000),
        "Kovács János",
        string.Create(CultureInfo.InvariantCulture, $"K-{row % 77:D4}"),
        "Gázolaj",
        row % 10 == 0 ? "Hosszabb megjegyzés a tételhez" : "Rendben",
        "Elfogadva",
    ];

    private static string Forints(int amount) => amount.ToString("N0", CultureInfo.InvariantCulture) + " Ft";
}
