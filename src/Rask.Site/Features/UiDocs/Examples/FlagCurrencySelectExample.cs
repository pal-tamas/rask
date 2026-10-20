namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FlagCurrencySelectExample : Component
{
    private static readonly (string Code, string Country, string Name)[] Currencies =
    [
        ("USD", "US", "US dollar"), ("EUR", "EU", "Euro"), ("CAD", "CA", "Canadian dollar"), ("GBP", "GB", "Pound sterling"),
        ("AUD", "AU", "Australian dollar"), ("JPY", "JP", "Japanese yen"), ("CNY", "CN", "Chinese yuan"), ("CHF", "CH", "Swiss franc"),
    ];

    private string _currency = "USD";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-5")[
            Div.Class("w-72")[
                Ui.Select.Value(_currency).Listbox.OnChange(code => { _currency = code ?? "USD"; })[
                    Currencies.Select(currency => Ui.SelectOption.Key(currency.Code).Value(currency.Code)[
                        Div.Class("flex items-center gap-2")[
                            Ui.Flag.Country(currency.Country).Xs,
                            Span.Class("font-medium")[currency.Code],
                            Span.Class("text-zinc-500 dark:text-zinc-400")[currency.Name]
                        ]
                    ])
                ]
            ],
            Ui.Text.Data("testid", "ui-flag-currency-says")[$"Billed in {_currency}."]
        ];
}
