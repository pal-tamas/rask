using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TableSteadyColumnsExample : Component
{
    private const int PageSize = 4;

    private static readonly Job[] Jobs =
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

    private string _sortBy = "date";
    private Ui.TableColumnDirection _direction = Ui.TableColumnDirection.Asc;
    private int _page = 1;
    private bool _hidden;

    protected override Component? Render()
    {
        var jobs = _hidden ? [] : Sorted();

        return Div.Class("grid gap-3")[
            Div[
                Ui.Button.Sm.Data("testid", "ui-fleet-toggle").OnClick(Toggle)[_hidden ? "Show the jobs" : "Hide the jobs"]
            ],
            // A width on the table, a width on every column but Job, and a floor for the phone.
            Ui.Table
                .Id("ui-fleet")
                .Class("w-full min-w-160")
                // A grid item grows to its content unless told it may not.
                .ContainerClass("min-w-0")
                .Paginate(Ui.Pagination
                    .Paginator(new UiPaginator { Page = _page, PerPage = PageSize, Total = jobs.Count })
                    .OnPage(page => { _page = page; }))[
                Ui.TableColumns[
                    Column("plate", "w-40")["Plate"],
                    Column("job", null)["Job"],
                    Column("date", "w-36")["Date"],
                    Ui.TableColumn.Class("w-24")[""]
                ],
                Ui.TableRows[
                    jobs.Skip((_page - 1) * PageSize).Take(PageSize).Select(job => Ui.TableRow.Key(job.Plate)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Class("truncate")[job.Plate],
                        Ui.TableCell.Class("truncate").Title(job.Name)[job.Name],
                        Ui.TableCell[job.Booked.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)],
                        Ui.TableCell.Class("py-0")[Ui.Button.Sm.Ghost["Open"]]
                    ])
                ]
            ]
        ];
    }

    private UiTableColumn Column(string column, string? width) =>
        Ui.TableColumn
            .Class(width)
            .Sortable()
            .Sorted(IsSortedBy(column))
            .Direction(_direction)
            .OnSort(() => SortBy(column));

    private bool IsSortedBy(string column) => string.Equals(_sortBy, column, StringComparison.Ordinal);

    private void Toggle()
    {
        _hidden = !_hidden;
        _page = 1;
    }

    private void SortBy(string column)
    {
        _direction = IsSortedBy(column) && _direction == Ui.TableColumnDirection.Asc
            ? Ui.TableColumnDirection.Desc
            : Ui.TableColumnDirection.Asc;
        _sortBy = column;
        _page = 1;
    }

    private List<Job> Sorted()
    {
        Func<Job, IComparable> key = _sortBy switch
        {
            "plate" => job => job.Plate,
            "job" => job => job.Name,
            _ => job => job.Booked,
        };

        return [.. _direction == Ui.TableColumnDirection.Asc ? Jobs.OrderBy(key) : Jobs.OrderByDescending(key)];
    }

    private sealed record Job(string Plate, string Name, DateOnly Booked);
}
