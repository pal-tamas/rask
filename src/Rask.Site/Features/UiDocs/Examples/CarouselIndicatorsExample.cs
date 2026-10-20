namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CarouselIndicatorsExample : Component
{
    private static readonly (string Said, string Name, string Role)[] Quotes =
    [
        ("“We shipped a polished UI in days, not weeks.”", "Graham Whitaker", " · Founder, Northwind"),
        ("“When I reach for a component, it just works.”", "Elise Rowan", " · Engineering Lead, Southline Studio"),
        ("“I stopped writing UI primitives entirely.”", "Jonah Vale", " · CTO, Alder & Co."),
    ];

    protected override Component? Render() =>
        Div.Class("mx-auto w-full max-w-sm")[
            Ui.Carousel.Class("w-full").Indicators().Arrows(false)[
                Quotes.Select(quote => Ui.CarouselSlide.Key(quote.Name).Class("w-full")[
                    Div.Class("flex flex-col items-center gap-4 text-center")[
                        Blockquote.Class("text-balance text-zinc-700 dark:text-zinc-300")[quote.Said],
                        Ui.Text.Sm[Ui.Text.Inline().Variant(Ui.TextVariant.Strong)[quote.Name], quote.Role]
                    ]
                ])
            ]
        ];
}
