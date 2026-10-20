namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CarouselOutsideExample : Component
{
    private static readonly string[] Slides = ["one", "two"];

    protected override Component? Render() =>
        Div.Class("mx-auto max-w-xs")[
            Ui.Carousel.Outside.TrackClass("rounded-lg")[
                Slides.Select(slide => Ui.CarouselSlide.Key(slide).Class("w-full")[
                    Div.Class("flex h-40 w-full items-center justify-center bg-zinc-100 text-sm font-medium text-zinc-500 dark:bg-white/10 dark:text-white/50")["Outside"]
                ])
            ]
        ];
}
