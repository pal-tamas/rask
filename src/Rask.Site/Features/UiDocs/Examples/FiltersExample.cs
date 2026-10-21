using System.Globalization;
using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

public sealed partial class UiFiltersPage
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private static readonly (string Number, string Driver, DateOnly Day, string Status)[] Orders =
    [
        ("#3102", "ada", Today, "open"),
        ("#3101", "grace", Today.AddDays(-1), "shipped"),
        ("#3099", "ada", Today.AddDays(-3), "shipped"),
        ("#3094", "alan", Today.AddDays(-8), "open"),
        ("#3090", "grace", Today.AddDays(-20), "returned"),
    ];

    [QueryParam("driver")] public string? Driver { get; set; }

    [QueryParam("period")] public UiDateRange? Period { get; set; }

    [QueryParam("status")] public string? Status { get; set; }

    // The list's own order, kept in the address beside the filters: no filter, so Clear all leaves it.
    [QueryParam("sort")] public string? Sort { get; set; }

    private Component FiltersExample()
    {
        var filters = Ui.Filters[
            Ui.Select.Bind(() => Driver).Label("Driver").Listbox.Searchable().Clearable().Placeholder("Anyone")[
                Ui.SelectOption.Value("ada")["Ada Lovelace"],
                Ui.SelectOption.Value("alan")["Alan Turing"],
                Ui.SelectOption.Value("grace")["Grace Hopper"]
            ],
            Ui.DatePicker.Range.Bind(() => Period).Label("Period").WithPresets().Clearable(),
            Ui.Select.Bind(() => Status).Label("Status").Listbox.Clearable().Placeholder("Any")[
                Ui.SelectOption.Value("open")["Open"],
                Ui.SelectOption.Value("shipped")["Shipped"],
                Ui.SelectOption.Value("returned")["Returned"]
            ]
        ];

        var rows = Orders.Where(order => (Driver is null || order.Driver == Driver)
                                         && (Period is not { } period || period.Contains(order.Day))
                                         && (Status is null || order.Status == Status));

        return Div.Class("space-y-4")[
            filters,
            filters.Badges,
            Ui.Table[
                Ui.TableColumns[
                    Ui.TableColumn["Order"],
                    Ui.TableColumn["Driver"],
                    Ui.TableColumn.Sortable().Sorted(Sort == "day").OnSort(() => Go.With("sort", Sort == "day" ? null : "day"))["Day"],
                    Ui.TableColumn["Status"]
                ],
                Ui.TableRows[
                    (Sort == "day" ? rows.OrderBy(order => order.Day) : rows).Select(order => Ui.TableRow.Key(order.Number)[
                        Ui.TableCell[order.Number],
                        Ui.TableCell[order.Driver],
                        Ui.TableCell[order.Day.ToString("MMM d", CultureInfo.InvariantCulture)],
                        Ui.TableCell[order.Status]
                    ])
                ]
            ]
        ];
    }
}
