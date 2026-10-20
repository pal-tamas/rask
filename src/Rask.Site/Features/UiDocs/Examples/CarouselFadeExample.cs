using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CarouselFadeExample : Component
{
    protected override Component? Render() =>
        Div.Class("px-6")[
            Ui.Carousel.Fade().Bleed()[
                Enumerable.Range(1, 5).Select(number => Ui.CarouselSlide.Key(number).Class("w-4/5 sm:w-1/2 md:w-1/3")[
                    Div.Class("flex aspect-2/1 items-center justify-center rounded-lg bg-zinc-100 text-2xl font-medium text-zinc-400 dark:bg-white/10 dark:text-white/40")[
                        number.ToString(CultureInfo.InvariantCulture)
                    ]
                ])
            ]
        ];
}
