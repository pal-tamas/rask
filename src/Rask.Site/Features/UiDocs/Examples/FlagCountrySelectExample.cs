namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FlagCountrySelectExample : Component
{
    private static readonly (string Code, string Name)[] Places =
    [
        ("US", "United States"), ("CA", "Canada"), ("MX", "Mexico"), ("BR", "Brazil"), ("GB", "United Kingdom"), ("FR", "France"),
        ("DE", "Germany"), ("NG", "Nigeria"), ("ZA", "South Africa"), ("IN", "India"), ("JP", "Japan"), ("AU", "Australia"),
    ];

    private string _country = "US";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-5")[
            Div.Class("w-72")[
                Ui.Select.Value(_country).Listbox.Searchable().OnChange(code => { _country = code ?? "US"; })[
                    Places.Select(place => Ui.SelectOption.Key(place.Code).Value(place.Code)[
                        Div.Class("flex items-center gap-2")[Ui.Flag.Country(place.Code).Xs, Span[place.Name]]
                    ])
                ]
            ],
            Ui.Text.Data("testid", "ui-flag-country-says")[$"Ships to {_country}."]
        ];
}
