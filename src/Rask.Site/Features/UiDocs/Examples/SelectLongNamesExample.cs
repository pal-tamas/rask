using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectLongNamesExample : Component
{
    private static readonly string[] Activities =
    [
        "Freight transport by road",
        "Sale, maintenance and repair of motor vehicles and light commercial vehicles, and the retail sale of their parts",
        "Other passenger land transport not elsewhere classified, including scheduled long-distance coach services",
        "Warehousing and storage",
        "Other transportation support activities, cargo handling and every other form of logistics service",
    ];

    // 1100 vehicles: a few say a detail under their name, and every fiftieth name is far too long for one line.
    private static readonly (int Id, string Name, string? Detail)[] Fleet =
    [
        .. Enumerable.Range(1, 1100).Select(id => (
            id,
            "Vehicle " + id.ToString("0000", CultureInfo.InvariantCulture) + (id % 50 == 0 ? " — articulated refrigerated semi-trailer with a tail lift" : ""),
            id % 7 == 0 ? "Trailer, " + (id * 3 % 40).ToString(CultureInfo.InvariantCulture) + " t" : null)),
    ];

    // 480 activity codes, most of them a long name and none with a detail.
    private static readonly (int Id, string Name)[] Codes =
    [
        .. Enumerable.Range(1, 480).Select(id => (id, (4000 + id).ToString(CultureInfo.InvariantCulture) + " " + Activities[(id + 1) % Activities.Length])),
    ];

    private int? _vehicle;

    private int? _code;

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Select.Value(_vehicle).OnChange(value => { _vehicle = value; }).Listbox.Searchable().Placeholder("Choose a vehicle...").Id("ui-select-long-details")[
                Fleet.Select(vehicle => Ui.SelectOption.Key(vehicle.Id).Value(vehicle.Id).Description(vehicle.Detail)[vehicle.Name])
            ],
            Ui.Select.Value(_code).OnChange(value => { _code = value; }).Listbox.Placeholder("Choose an activity...").Id("ui-select-long-codes")[
                Codes.Select(code => Ui.SelectOption.Key(code.Id).Value(code.Id)[code.Name])
            ],
            Ui.Text.Data("testid", "ui-select-long-names-state")[$"Vehicle: {Said(_vehicle)}. Activity: {Said(_code)}."]
        ];

    private static string Said(int? id) => id?.ToString(CultureInfo.InvariantCulture) ?? "none";
}
