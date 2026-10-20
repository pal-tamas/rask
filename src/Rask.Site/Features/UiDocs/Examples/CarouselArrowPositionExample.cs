namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CarouselArrowPositionExample : Component
{
    private static readonly string[] Slides = ["one", "two"];

    protected override Component? Render() =>
        Div.Class("mx-auto max-w-xs space-y-6")[
            Ui.Carousel.Key("inside").TrackClass("rounded-lg")[Labelled("Inside")],
            Ui.Carousel.Key("overlap").Overlap.TrackClass("rounded-lg")[Labelled("Overlap")]
        ];

    private static IEnumerable<Component> Labelled(string label) =>
        Slides.Select(slide => Ui.CarouselSlide.Key(label + "-" + slide).Class("w-full")[
            Div.Class("flex h-40 w-full items-center justify-center bg-zinc-100 text-sm font-medium text-zinc-500 dark:bg-white/10 dark:text-white/50")[label]
        ]);
}
