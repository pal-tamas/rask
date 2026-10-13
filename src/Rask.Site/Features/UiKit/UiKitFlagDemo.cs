namespace Rask.Site.Features.UiKit;

/// <summary>
///     Flux UI's flag page, example for example, drawn with <c>Ui.Flag</c>.
/// </summary>
/// <remarks>
///     The drawings are files in <c>wwwroot/flags</c>, copied there by the build because this project says
///     <c>RaskUiWriteFlags</c>: the page fetches the ones it shows and none of the others.
/// </remarks>
public sealed partial class UiKitFlagDemo : Component
{
    // A flag of the page's own, for "Custom flags": Src takes any image.
    private const string Pennant =
        "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 3 2'%3E%3Cpath fill='%23fff' d='M0 0h3v2H0z'/%3E"
        + "%3Cpath fill='%23be123c' d='M0 1.6h3V2H0z'/%3E%3Ccircle cx='.6' cy='.6' r='.3' fill='%23be123c'/%3E%3C/svg%3E";

    private static readonly string[] Rectangles = ["US", "EU", "IN", "BR", "DE", "GB", "PH"];

    private static readonly string[] Circles = ["US", "FR", "JP", "BR", "NG"];

    private static readonly (string Code, string Name)[] Places =
    [
        ("US", "United States"), ("CA", "Canada"), ("MX", "Mexico"), ("BR", "Brazil"), ("GB", "United Kingdom"), ("FR", "France"),
        ("DE", "Germany"), ("NG", "Nigeria"), ("ZA", "South Africa"), ("IN", "India"), ("JP", "Japan"), ("AU", "Australia"),
    ];

    private static readonly (string Code, string Country, string Name)[] Currencies =
    [
        ("USD", "US", "US dollar"), ("EUR", "EU", "Euro"), ("CAD", "CA", "Canadian dollar"), ("GBP", "GB", "Pound sterling"),
        ("AUD", "AU", "Australian dollar"), ("JPY", "JP", "Japanese yen"), ("CNY", "CN", "Chinese yuan"), ("CHF", "CH", "Swiss franc"),
    ];

    private static readonly (string Code, string Name, string Visitors)[] Visitors =
    [
        ("ID", "Indonesia", "3.6K"), ("US", "United States", "2.6K"), ("IN", "India", "2.4K"), ("BR", "Brazil", "2.0K"),
        ("DE", "Germany", "1.9K"), ("GB", "United Kingdom", "1.4K"), ("PH", "Philippines", "1.0K"),
    ];

    private string _country = "US";
    private string _currency = "USD";

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Data(Testid("ui-flag")).Class("space-y-6")[
            Example("basic", "Flag", [.. Rectangles.Select(code => Ui.Flag.Key("flag-" + code).Country(code))]),
            Example(
                "sizes",
                "Sizes",
                Ui.Flag.Key("xl").Country("US").Xl,
                Ui.Flag.Key("lg").Country("US").Lg,
                Ui.Flag.Key("md").Country("US").Md,
                Ui.Flag.Key("sm").Country("US"),
                Ui.Flag.Key("xs").Country("US").Xs),
            Example("circle", "Circle", [.. Circles.Select(code => Ui.Flag.Key("circle-" + code).Circle().Country(code))]),
            Example("country", "Country select", CountrySelect(), Said("country", $"Ships to {_country}.")),
            Example("currency", "Currency select", CurrencySelect(), Said("currency", $"Billed in {_currency}.")),
            Example("analytics", "Analytics card", Analytics()),
            Example("custom", "Custom flags", Ui.Flag.Src(Pennant).Alt("Pennant").Xl),
            Example(
                "accessibility",
                "Accessibility",
                Div.Key("decorative").Class("flex items-center gap-2 text-sm")[Ui.Flag.Country("CA"), Span["Canada"]],
                Ui.Flag.Key("informative").Country("CA").Alt("Canada"))
        ];

    private Component CountrySelect() =>
        Div.Key("country").Class("w-72")[
            Ui.Select.Value(_country).Listbox.Searchable().OnChange(code => { _country = code ?? "US"; })[
                Places.Select(place =>
                    Ui.SelectOption.Key(place.Code).Value(place.Code)[
                        Div.Class("flex items-center gap-2")[Ui.Flag.Country(place.Code).Xs, Span[place.Name]]
                    ])
            ]
        ];

    private Component CurrencySelect() =>
        Div.Key("currency").Class("w-72")[
            Ui.Select.Value(_currency).Listbox.OnChange(code => { _currency = code ?? "USD"; })[
                Currencies.Select(currency =>
                    Ui.SelectOption.Key(currency.Code).Value(currency.Code)[
                        Div.Class("flex items-center gap-2")[
                            Ui.Flag.Country(currency.Country).Xs,
                            Span.Class("font-medium")[currency.Code],
                            Span.Class("text-zinc-500 dark:text-zinc-400")[currency.Name]
                        ]
                    ])
            ]
        ];

    private static Component Analytics() =>
        Div.Class("w-full max-w-sm")[
            Ui.Card.Soft[
                Div.Class("flex items-baseline justify-between gap-4")[
                    Ui.Heading["Countries"],
                    Ui.Text.Class("font-medium")["Visitors"]
                ],
                Div.Class("mt-6 space-y-5")[
                    Visitors.Select(country =>
                        Div.Key(country.Code).Class("flex items-center gap-3")[
                            Ui.Flag.Country(country.Code).Md.Class("w-7"),
                            Ui.Text.Variant(Ui.TextVariant.Strong).Class("min-w-0 flex-1 truncate")[country.Name],
                            Ui.Text.Variant(Ui.TextVariant.Strong).Class("font-medium")[country.Visitors]
                        ])
                ]
            ]
        ];

    private static Component Said(string name, string words) =>
        Span.Key("said").Data(Testid("ui-flag-" + name + "-says")).Class("text-sm text-ui-muted")[words];

    private static Component Example(string name, string title, params Component?[] flags) =>
        Div.Key(name).Data(Testid("ui-flag-" + name)).Class("space-y-2")[
            H3.Class("text-sm font-medium text-ui-muted")[title],
            Div.Class("flex flex-wrap items-center gap-5")[flags]
        ];

    private static AttrBag Testid(string value) => new("testid", value);
}
