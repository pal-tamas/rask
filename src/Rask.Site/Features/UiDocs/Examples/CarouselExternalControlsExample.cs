namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CarouselExternalControlsExample : Component
{
    private static readonly (string Name, string Price, string Rating)[] Stays =
    [
        ("Cottage in Mendocino", "$680", "5.0"), ("Lodge in the Thousand Islands", "$370", "4.96"),
        ("House in the Adirondacks", "$550", "4.88"), ("House in Cape Cod", "$840", "5"),
    ];

    protected override Component? Render() =>
        Div.Class("px-6")[
            Div.Class("mb-4 flex items-center justify-between")[
                Div[Ui.Heading.Lg["Popular stays"], Ui.Text["Handpicked homes in beautiful places"]],
                Div[Ui.CarouselControls.Name("popular-stays")]
            ],
            Ui.Carousel.Name("popular-stays").Bleed().Arrows(false)[
                Stays.Select(stay => Ui.CarouselSlide.Key(stay.Name).Class("w-4/5 sm:w-1/2 md:w-1/3")[
                    Div.Class("aspect-2/1 rounded-lg bg-zinc-100 dark:bg-white/10"),
                    Ui.Heading.Class("mt-2")[stay.Name],
                    Div.Class("mt-1 flex items-center gap-0.5")[
                        Ui.Text.Class("text-xs")[stay.Price, " for 2 nights ・"],
                        Ui.Icon.Name(Ui.IconName.Star).Variant(Ui.IconVariant.Micro).Class("size-3"),
                        Ui.Text.Class("text-xs text-zinc-800 dark:text-white")[stay.Rating]
                    ]
                ])
            ]
        ];
}
