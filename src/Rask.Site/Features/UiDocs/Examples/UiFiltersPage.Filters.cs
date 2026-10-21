using System.Globalization;
using Rask.Core.Routing;
using Rask.Querying;

namespace Rask.Site.Features.UiDocs;

public sealed partial class UiFiltersPage
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);

    private static readonly Order[] Orders =
    [
        new("#3102", "Ada Lovelace", Today, "Open"),
        new("#3101", "Grace Hopper", Today.AddDays(-1), "Shipped"),
        new("#3099", "Ada Lovelace", Today.AddDays(-3), "Shipped"),
        new("#3094", "Alan Turing", Today.AddDays(-8), "Open"),
        new("#3090", "Grace Hopper", Today.AddDays(-20), "Returned"),
    ];

    [QueryParam("driver")] public string? Driver { get; set; }

    [QueryParam("period")] public UiDateRange? Period { get; set; }

    [QueryParam("status")] public string? Status { get; set; }

    // The list's own order, kept in the address beside the filters: no filter, so Clear all leaves it.
    [QueryParam("sort")] public string? Sort { get; set; }

    private bool ByDay => string.Equals(Sort, "day", StringComparison.Ordinal);

    // The rows, read again whenever the address changes what they are filtered or sorted by.
    private Query<Order[]> Filtered => field ??= QueryClient.Query(
        QueryKey.Of("ui-filters"), () => (Driver, Period, Status, ByDay), (asked, ct) => Read(asked, ct));

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
                Ui.SelectOption.Value("Open")["Open"],
                Ui.SelectOption.Value("Shipped")["Shipped"],
                Ui.SelectOption.Value("Returned")["Returned"]
            ]
        ];

        return Ui.Card[
            Div.Class("flex items-center justify-between gap-4")[
                Div[
                    Ui.Heading["Recent orders"],
                    Ui.Text.Class("mt-1")["What each driver delivered."]
                ],
                filters
            ],
            Div.Class("mt-4")[filters.Badges],
            Ui.Table.Bleed().ContainerClass("mt-6").Of(Filtered)[
                Ui.TableColumns[
                    Ui.TableColumn["Order"],
                    Ui.TableColumn["Driver"],
                    Ui.TableColumn.Sortable().Sorted(ByDay).OnSort(() => Go.With("sort", ByDay ? null : "day"))["Day"],
                    Ui.TableColumn["Status"]
                ],
                Ui.TableRows[
                    (Filtered.Data ?? []).Select(order => Ui.TableRow.Key(order.Number)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[order.Number],
                        Ui.TableCell[order.Driver],
                        Ui.TableCell[order.Day.ToString("MMM d", CultureInfo.InvariantCulture)],
                        Ui.TableCell[order.Status]
                    ])
                ]
            ]
        ];
    }

    // A slow read, so the skeleton has time to be seen.
    private static async Task<Order[]> Read((string? Driver, UiDateRange? Period, string? Status, bool ByDay) asked, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(600), ct);
        var rows = Orders.Where(order => (asked.Driver is null || order.Driver.StartsWith(asked.Driver, StringComparison.OrdinalIgnoreCase))
                                         && (asked.Period is not { } period || period.Contains(order.Day))
                                         && (asked.Status is null || string.Equals(order.Status, asked.Status, StringComparison.Ordinal)));
        return [.. asked.ByDay ? rows.OrderBy(order => order.Day) : rows];
    }

    private sealed record Order(string Number, string Driver, DateOnly Day, string Status);
}
