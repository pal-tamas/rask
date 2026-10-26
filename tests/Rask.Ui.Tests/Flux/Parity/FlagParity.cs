using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/flag</c>, example for example.
/// </summary>
/// <remarks>
///     <para>
///     The first two of the page's three "Examples" put flags in the options of a listbox select, written
///     here with the kit's own; the third, the analytics card, is compared whole.
///     </para>
///     <para>
///     The tool measures the frame a flag is drawn in, not the picture: which file each code draws is held
///     by the unit tests, and where the artwork comes from by <c>scripts/flux/flags.mjs</c>.
///     </para>
/// </remarks>
public sealed partial class FlagParity : FluxParity
{
    // What an app's own Tailwind build emits for the classes the analytics card hands its parts.
    private const string AppUtilities =
        "<style>.w-7{width:28px}.parity-medium{font-weight:500}.parity-count{font-weight:500;flex-shrink:0}"
        + ".parity-name{min-width:0;flex:1 1 0%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}"
        + ".parity-code{font-weight:500}.parity-muted{color:oklch(55.2% .016 285.938)}"
        // The rendered example lightens the name in dark, which its markdown leaves out.
        + "@media (prefers-color-scheme:dark){.parity-muted{color:oklch(70.5% .015 286.067)}}</style>";

    // A flag of the app's own, for "Custom flags": any image does, the frame is what is measured.
    private const string OwnFlag =
        "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 3 2'%3E%3Cpath fill='%23fff' d='M0 0h3v2H0z'/%3E%3Cpath fill='%23b22' d='M0 1.7h3V2H0z'/%3E%3C/svg%3E";

    private static readonly (string Code, string Name, string Visitors)[] Countries =
    [
        ("ID", "Indonesia", "3.6K"), ("US", "United States", "2.6K"), ("IN", "India", "2.4K"), ("BR", "Brazil", "2.0K"),
        ("DE", "Germany", "1.9K"), ("GB", "United Kingdom", "1.4K"), ("PH", "Philippines", "1.0K"),
    ];

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

    private const string Row = "display:flex;align-items:center;gap:12px";

    private const string Option = "display:flex;align-items:center;gap:8px";

    public override string Page => "flag";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Centred(
            "gap:20px",
            new[] { "US", "EU", "IN", "BR", "DE", "GB", "PH" }.Select(code => (Component)Ui.Flag.Key(code).Country(code)).ToArray()));

        yield return ("sizes", Centred(
            "gap:24px",
            Ui.Flag.Country("US").Xl,
            Ui.Flag.Country("US").Lg,
            Ui.Flag.Country("US").Md,
            Ui.Flag.Country("US"),
            Ui.Flag.Country("US").Xs));

        yield return ("circle", Centred(
            "gap:20px",
            new[] { "US", "FR", "JP", "BR", "NG" }.Select(code => (Component)Ui.Flag.Key(code).Circle().Country(code)).ToArray()));

        yield return ("examples", Centred("", Div.Style("width:288px")[
            Ui.Select.Value("US").Listbox.Searchable()[
                Places.Select(place =>
                    Ui.SelectOption.Key(place.Code).Value(place.Code)[
                        Div.Style(Option)[Ui.Flag.Country(place.Code).Xs, Span[place.Name]]
                    ])
            ]
        ]));

        yield return ("examples", Centred("", Raw.Value(AppUtilities), Div.Style("width:288px")[
            Ui.Select.Value("USD").Listbox[
                Currencies.Select(currency =>
                    Ui.SelectOption.Key(currency.Code).Value(currency.Code)[
                        Div.Style(Option)[
                            Ui.Flag.Country(currency.Country).Xs,
                            Span.Class("parity-code")[currency.Code],
                            Span.Class("parity-muted")[currency.Name]
                        ]
                    ])
            ]
        ]));

        yield return ("examples", Centred(
            "width:100%;max-width:384px;display:block",
            Raw.Value(AppUtilities),
            Ui.Card.Soft[
                Div.Style("display:flex;align-items:baseline;justify-content:space-between;gap:16px")[
                    Ui.Heading["Countries"],
                    Ui.Text.Class("parity-medium")["Visitors"]
                ],
                Div.Style("margin-top:24px")[
                    Countries.Select((country, row) =>
                        Div.Key(country.Code).Style(row == Countries.Length - 1 ? Row : Row + ";margin-bottom:20px")[
                            Ui.Flag.Country(country.Code).Md.Class("w-7"),
                            Ui.Text.Variant(Ui.TextVariant.Strong).Class("parity-name")[country.Name],
                            Ui.Text.Variant(Ui.TextVariant.Strong).Class("parity-count")[country.Visitors]
                        ])
                ]
            ]));

        yield return ("custom-flags", Centred("", Div[Ui.Flag.Src(OwnFlag).Alt("California").Xl]));
    }

    private static Component Centred(string row, params Component[] items) =>
        Div.Style("display:flex;justify-content:center")[
            Div.Style("display:flex;align-items:center;" + row)[items]
        ];
}
