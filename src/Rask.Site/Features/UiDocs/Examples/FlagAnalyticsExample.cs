namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FlagAnalyticsExample : Component
{
    private static readonly (string Code, string Name, string Visitors)[] Countries =
    [
        ("ID", "Indonesia", "3.6K"), ("US", "United States", "2.6K"), ("IN", "India", "2.4K"), ("BR", "Brazil", "2.0K"),
        ("DE", "Germany", "1.9K"), ("GB", "United Kingdom", "1.4K"), ("PH", "Philippines", "1.0K"),
    ];

    protected override Component? Render() =>
        Ui.Card.Soft.Class("w-full max-w-sm")[
            Div.Class("flex items-baseline justify-between gap-4")[
                Ui.Heading["Countries"],
                Ui.Text.Class("font-medium")["Visitors"]
            ],
            Div.Class("mt-6 space-y-5")[
                Countries.Select(country => Div.Key(country.Code).Class("flex items-center gap-3")[
                    Ui.Flag.Country(country.Code).Md.Class("w-7"),
                    Ui.Text.Variant(Ui.TextVariant.Strong).Class("min-w-0 flex-1 truncate")[country.Name],
                    Ui.Text.Variant(Ui.TextVariant.Strong).Class("font-medium")[country.Visitors]
                ])
            ]
        ];
}
