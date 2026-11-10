using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectLongListExample : Component
{
    private static readonly string[] Makes = ["DAF", "Iveco", "MAN", "Mercedes", "Scania", "Volvo"];

    // A fleet: 1200 vehicles, every one an option. Past a hundred the list draws only the rows you scroll to.
    private static readonly (int Id, string Name)[] Fleet =
    [
        .. Enumerable.Range(1, 1200).Select(id => (id, Makes[(id - 1) % Makes.Length] + " " + id.ToString("0000", CultureInfo.InvariantCulture))),
    ];

    private int? _vehicle;

    private int? _trailer;

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Select.Value(_vehicle).OnChange(value => { _vehicle = value; }).Listbox.Searchable().Placeholder("Choose a vehicle...").Id("ui-select-long-list")[
                Fleet.Select(vehicle => Ui.SelectOption.Key(vehicle.Id).Value(vehicle.Id)[vehicle.Name])
            ],
            // No search field: type the first letters of a vehicle to jump to it, wherever it is in the list.
            Ui.Select.Value(_trailer).OnChange(value => { _trailer = value; }).Listbox.Placeholder("Choose a trailer...").Id("ui-select-long-plain")[
                Fleet.Select(vehicle => Ui.SelectOption.Key(vehicle.Id).Value(vehicle.Id)[vehicle.Name])
            ],
            Ui.Text.Data("testid", "ui-select-long-state")[$"Vehicle: {Said(_vehicle)}. Trailer: {Said(_trailer)}."]
        ];

    private static string Said(int? id) => id?.ToString(CultureInfo.InvariantCulture) ?? "none";
}
