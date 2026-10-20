namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CarouselAutoplayExample : Component
{
    private static readonly string[] Scenes = ["Seascape", "Woodscape", "Landscape"];

    protected override Component? Render() =>
        Div.Class("mx-auto w-full max-w-sm")[
            Ui.Carousel.Autoplay().AutoplayInterval(3500).Indicators().TrackClass("rounded-lg")[
                Scenes.Select(scene => Ui.CarouselSlide.Key(scene).Class("w-full")[
                    Div.Class("flex aspect-16/9 items-center justify-center rounded-lg bg-zinc-100 text-sm font-medium text-zinc-500 dark:bg-white/10 dark:text-white/50")[scene]
                ])
            ]
        ];
}
