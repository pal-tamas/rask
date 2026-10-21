using System.Globalization;

namespace Rask.Site.Features;

// A report written with Flux pieces, as an app writes one to print: Pdf.From(Routes.RefundReport(id)) on a server
// turns exactly this into the PDF the demo downloads. The table's heading is a <thead>, so Chromium repeats it on
// every printed page by itself; break-inside-avoid keeps a row from being cut across two.
public sealed partial class RefundReport : Component
{
    private static readonly NumberFormatInfo Hungarian = new() { NumberGroupSeparator = " ", NumberDecimalSeparator = "," };

    protected override Component? Render() =>
        Div.Class("refund-report")[
            Ui.Heading.Level(1).Size(Ui.HeadingSize.Xl)["Gázolaj-visszatérítés — 2026. III. negyedév"],
            Ui.Text.Class("mb-4")["Kovács és Társa Fuvarozó Kft. · Győr · jövedéki szám HU00012345"],
            Ui.Table[
                Ui.TableColumns[
                    Ui.TableColumn["Dátum"],
                    Ui.TableColumn["Számlaszám"],
                    Ui.TableColumn["Kút"],
                    Ui.TableColumn.Align(Ui.Align.End)["Liter"],
                    Ui.TableColumn.Align(Ui.Align.End)["Visszatérítés"]
                ],
                RefundData.Vehicles.Select(Group)
            ],
            Ui.Heading.Level(2).Size(Ui.HeadingSize.Lg).Class("mt-6")[
                $"Összesen: {Amount(RefundData.Vehicles.Sum(v => v.Rows.Sum(r => r.Refund)))} Ft"]
        ];

    private static Component Group(RefundData.Vehicle vehicle) =>
        Ui.TableRows.Key(vehicle.Plate)[
            Ui.TableRow.Class("break-inside-avoid")[
                Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[vehicle.Plate],
                Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[vehicle.Model],
                Ui.TableCell[""],
                Ui.TableCell[""],
                Ui.TableCell[""]
            ],
            vehicle.Rows.Select(row => Ui.TableRow.Key(row.Invoice).Class("break-inside-avoid")[
                Ui.TableCell[row.Day.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture)],
                Ui.TableCell[row.Invoice],
                Ui.TableCell[row.Station],
                Ui.TableCell.Align(Ui.Align.End)[row.Litres.ToString("N1", Hungarian)],
                Ui.TableCell.Align(Ui.Align.End)[$"{Amount(row.Refund)} Ft"]
            ]),
            Ui.TableRow.Class("break-inside-avoid")[
                Ui.TableCell[""],
                Ui.TableCell[""],
                Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[$"{vehicle.Plate} összesen"],
                Ui.TableCell.Align(Ui.Align.End).Variant(Ui.TableCellVariant.Strong)[
                    vehicle.Rows.Sum(r => r.Litres).ToString("N1", Hungarian)],
                Ui.TableCell.Align(Ui.Align.End).Variant(Ui.TableCellVariant.Strong)[
                    $"{Amount(vehicle.Rows.Sum(r => r.Refund))} Ft"]
            ]
        ];

    private static string Amount(decimal value) => value.ToString("N0", Hungarian);
}
