using System.Globalization;

namespace Rask.Site.Features.UiKit;

// Columns that stay put: the table is given a width, each column but one a width of its own, and a long
// value is cut where its column ends. Paging, sorting and an empty list then move nothing.
public sealed partial class UiKitDataDisplayDemo
{
    private const int FleetPageSize = 4;

    private static readonly Vehicle[] Fleet =
    [
        new("ABC-123", "Tyre change", new(2026, 3, 2)),
        new("KLM-456", "Oil", new(2026, 3, 9)),
        new("RST-789", "MOT", new(2026, 3, 11)),
        new("XYZ-012", "Wipers", new(2026, 3, 12)),
        new("AA-BB-001", "Full service with brake pads, brake discs, a coolant flush, both wiper arms, the cabin filter and a wash, as agreed with the driver on the phone", new(2026, 4, 1)),
        new("AA-BB-002", "Windscreen replaced after the stone chip on the motorway spread across the driver's side overnight in the frost", new(2026, 4, 3)),
        new("AA-BB-003", "Gearbox", new(2026, 4, 7)),
        new("AA-BB-004", "Insurance claim: rear bumper, tailgate, both rear lights and the parking sensors, waiting on the assessor's second visit", new(2026, 4, 8)),
        new("ZZ-9", "Bulb", new(2026, 5, 20)),
        new("ZZ-10", "Battery", new(2026, 5, 21)),
    ];

    private string _fleetSort = "date";
    private Ui.TableColumnDirection _fleetDirection = Ui.TableColumnDirection.Asc;
    private int _fleetPage = 1;
    private bool _fleetHidden;

    private Component StableColumnsSection() =>
        Section(
            "Table columns that stay put",
            "A table sizes its columns to what is in them, so they shift from page to page. Give the table a width "
            + "and each column but one a width, and they hold: the one without takes the rest, and a long value "
            + "ends in an ellipsis with the whole of it in its tooltip. On a phone the table scrolls inside its own box.",
            Div.Data(Testid("ui-table-stable")).Class("grid gap-3")[
                Div[
                    Ui.Button.Sm.Data(Testid("ui-fleet-toggle")).OnClick(ToggleFleet)[
                        _fleetHidden ? "Show the jobs" : "Hide the jobs"
                    ]
                ],
                StableColumns()
            ]);

    private Component StableColumns()
    {
        var jobs = _fleetHidden ? [] : SortedFleet();

        return Ui.Table
            .Id("ui-fleet")
            .Class("w-full min-w-160")
            .Paginate(Ui.Pagination
                .Paginator(new UiPaginator { Page = _fleetPage, PerPage = FleetPageSize, Total = jobs.Count })
                .OnPage(page => { _fleetPage = page; }))[
            Ui.TableColumns[
                FleetColumn("plate", "w-40")["Plate"],
                FleetColumn("job", null)["Job"],
                FleetColumn("date", "w-36")["Date"],
                Ui.TableColumn.Class("w-24")[""]
            ],
            Ui.TableRows[
                jobs.Skip((_fleetPage - 1) * FleetPageSize).Take(FleetPageSize).Select(vehicle => Ui.TableRow.Key(vehicle.Plate)[
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Class("truncate")[vehicle.Plate],
                    Ui.TableCell.Class("truncate").Title(vehicle.Job)[vehicle.Job],
                    Ui.TableCell[vehicle.Booked.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)],
                    Ui.TableCell.Class("py-0")[Ui.Button.Sm.Ghost["Open"]]
                ])
            ]
        ];
    }

    private UiTableColumn FleetColumn(string column, string? width) =>
        Ui.TableColumn
            .Class(width)
            .Sortable()
            .Sorted(string.Equals(_fleetSort, column, StringComparison.Ordinal))
            .Direction(_fleetDirection)
            .OnSort(() => SortFleetBy(column));

    private void ToggleFleet()
    {
        _fleetHidden = !_fleetHidden;
        _fleetPage = 1;
    }

    private void SortFleetBy(string column)
    {
        _fleetDirection = string.Equals(_fleetSort, column, StringComparison.Ordinal) && _fleetDirection == Ui.TableColumnDirection.Asc
            ? Ui.TableColumnDirection.Desc
            : Ui.TableColumnDirection.Asc;
        _fleetSort = column;
        _fleetPage = 1;
    }

    private List<Vehicle> SortedFleet()
    {
        Func<Vehicle, IComparable> key = _fleetSort switch
        {
            "plate" => vehicle => vehicle.Plate,
            "job" => vehicle => vehicle.Job,
            _ => vehicle => vehicle.Booked,
        };

        return [.. _fleetDirection == Ui.TableColumnDirection.Asc ? Fleet.OrderBy(key) : Fleet.OrderByDescending(key)];
    }

    private sealed record Vehicle(string Plate, string Job, DateOnly Booked);
}
