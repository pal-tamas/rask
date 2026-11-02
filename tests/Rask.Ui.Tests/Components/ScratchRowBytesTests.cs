namespace Rask.UiTests.Components;

// Scratch, never committed: bytes a 13-column row writes, and a page of a 13-column table to photograph.
public sealed class ScratchRowBytesTests
{
    [Fact]
    public void Scratch_prints_the_bytes_of_a_thirteen_column_row()
    {
        var plain = Ui.TableRow[Enumerable.Range(0, 13).Select(i => Ui.TableCell.Key(i)["x"])].ToHtml();
        var mixed = Row(1).ToHtml();
        var table = Ui.Table.Class("w-full")[
            Ui.TableColumns[
                Ui.TableColumn["Dátum"],
                Ui.TableColumn["Kút"],
                Ui.TableColumn["Megnevezés"],
                Ui.TableColumn.End["Bevétel"],
                Ui.TableColumn.End["Kiadás"],
                Ui.TableColumn.End["Mennyiség"],
                Ui.TableColumn["Egység"],
                Ui.TableColumn["Aktív"],
                Ui.TableColumn["Zárolt"],
                Ui.TableColumn.Center["Osztály"],
                Ui.TableColumn["Megjegyzés"],
                Ui.TableColumn.End.Sortable().Sorted()["Sor"],
                Ui.TableColumn.End[Markup.Span.Class("sr-only")["Műveletek"]]
            ],
            Ui.TableRows[Enumerable.Range(1, 8).Select(Row)]
        ].ToHtml();
        var dir = Environment.GetEnvironmentVariable("SCRATCH_DIR") ?? "/tmp";

        File.WriteAllText(Path.Combine(dir, "row.txt"),
            $"plain row: {plain.Length} chars\nmixed row: {mixed.Length} chars\n{plain}\n{mixed}\n");
        File.WriteAllText(Path.Combine(dir, "table13.html"),
            "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><style>"
            + UiStylesheet.Css
            + "body{margin:0;padding:24px;background:#fff;font-family:Inter,sans-serif}.dark body{background:#27272a}</style></head><body>"
            + table + "</body></html>");
        Assert.True(plain.Length > 0);
    }

    private static global::Rask.Core.Component Row(int id) =>
        Ui.TableRow.Key(id)[
            Ui.TableCell.Variant(Ui.TableCellVariant.Strong)["2026-10-0" + id.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            Ui.TableCell["Kút " + id.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            Ui.TableCell.Class("truncate max-w-60").Title("Hosszú megnevezés, amely nem fér el")["Hosszú megnevezés, amely nem fér el"],
            Ui.TableCell.End["12 345"],
            Ui.TableCell.End["6 789"],
            Ui.TableCell.End["1 000"],
            Ui.TableCell["l"],
            Ui.TableCell["Igen"],
            Ui.TableCell["Nem"],
            Ui.TableCell.Center["A"],
            Ui.TableCell["Megjegyzés"],
            Ui.TableCell.End[(id * 3).ToString(System.Globalization.CultureInfo.InvariantCulture)],
            Ui.TableCell.End.Class("py-0")[Ui.Button.Ghost.Sm.Square(true).Icon(Ui.IconName.Pencil).Aria("label", "Módosítás")]
        ];
}
