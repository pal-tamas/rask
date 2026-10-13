using System.Globalization;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     Flux UI's carousel page, example for example, drawn with <c>Ui.Carousel</c>.
/// </summary>
/// <remarks>
///     The slides are placeholders where Flux's page shows photographs. Nothing here holds state: the browser
///     scrolls and snaps, and Rask's runtime moves the arrows, the indicators and autoplay with it. The first
///     eight carousels stand in the order of Flux's page, which <c>scripts/flux/parity-carousel.mjs --live</c>
///     walks; what Flux's page does not draw comes after them.
/// </remarks>
public sealed partial class UiKitCarouselDemo : Component
{
    private const string Box = "flex items-center justify-center bg-zinc-100 font-medium dark:bg-white/10";

    private static readonly (string Said, string Name, string Role)[] Quotes =
    [
        ("“We shipped a polished UI in days, not weeks.”", "Graham Whitaker", " · Founder, Northwind"),
        ("“When I reach for a component, it just works.”", "Elise Rowan", " · Engineering Lead, Southline Studio"),
        ("“I stopped writing UI primitives entirely.”", "Jonah Vale", " · CTO, Alder & Co."),
    ];

    private static readonly string[] SceneNames = ["Seascape", "Woodscape", "Landscape"];

    private static readonly (string Name, string Price, string Rating)[] Stays =
    [
        ("Cottage in Mendocino", "$680", "5.0"), ("Lodge in the Thousand Islands", "$370", "4.96"),
        ("House in the Adirondacks", "$550", "4.88"), ("House in Cape Cod", "$840", "5"),
    ];

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Data(Testid("ui-carousel")).Class("max-w-2xl space-y-8")[
            Example("basic", "Carousel", Narrow(Ui.Carousel.TrackClass("rounded-lg")[Scenes("basic")])),
            Example(
                "indicators",
                "Indicators",
                Narrow(Ui.Carousel.Class("w-full").Indicators().Arrows(false)[
                    Quotes.Select(quote =>
                        Ui.CarouselSlide.Key(quote.Name).Class("w-full")[
                            Div.Class("flex flex-col items-center gap-4 text-center")[
                                Blockquote.Class("text-balance text-zinc-700 dark:text-zinc-300")[quote.Said],
                                Div.Class("text-sm text-zinc-500")[
                                    Span.Class("font-semibold text-zinc-700 dark:text-zinc-200")[quote.Name], quote.Role
                                ]
                            ]
                        ])
                ])),
            Example(
                "autoplay",
                "Autoplay",
                Narrow(Ui.Carousel.Autoplay().AutoplayInterval(3500).Indicators().TrackClass("rounded-lg")[Scenes("auto")])),
            Example("external", "External controls", ExternalControls()),
            Example(
                "position",
                "Arrow position",
                Div.Class("mx-auto max-w-xs space-y-6")[
                    Ui.Carousel.Key("inside").TrackClass("rounded-lg")[Labelled("Inside")],
                    Ui.Carousel.Key("overlap").Overlap.TrackClass("rounded-lg")[Labelled("Overlap")]
                ]),
            Example("fade", "Fade", Div.Class("px-6")[Ui.Carousel.Fade().Bleed()[Numbered("fade", 5)]]),
            Example("page", "Page advance", Div.Class("px-6")[Ui.Carousel.Page.Bleed()[Numbered("page", 10)]]),
            Example(
                "outside",
                "Arrows outside",
                Div.Class("mx-auto max-w-xs")[Ui.Carousel.Outside.TrackClass("rounded-lg")[Labelled("Outside")]]),
            Example("rewind", "Rewind", Narrow(Ui.Carousel.Rewind.Mandatory.TrackClass("rounded-lg")[Scenes("rewind")])),
            Example("disabled", "Disabled", Narrow(Ui.Carousel.Disabled().Indicators().TrackClass("rounded-lg")[Scenes("disabled")]))
        ];

    private static Component ExternalControls() =>
        Div.Class("px-6")[
            Div.Class("mb-4 flex items-center justify-between")[
                Div[Ui.Heading.Lg["Popular stays"], Ui.Text["Handpicked homes in beautiful places"]],
                Div[Ui.CarouselControls.Name("popular-stays")]
            ],
            Ui.Carousel.Name("popular-stays").Bleed().Arrows(false)[
                Stays.Select(stay =>
                    Ui.CarouselSlide.Key(stay.Name).Class("w-4/5 sm:w-1/2 md:w-1/3")[
                        Div.Class("aspect-2/1 rounded-lg bg-zinc-100 dark:bg-white/10"),
                        Ui.Heading.Class("mt-2")[stay.Name],
                        Div.Class("mt-1 flex items-center gap-0.5")[
                            Ui.Text.Class("text-xs")[stay.Price, " for 2 nights ・"],
                            Div.Class("flex items-center gap-0.5")[
                                Ui.Icon.Name(Ui.IconName.Star).Variant(Ui.IconVariant.Micro).Class("size-3"),
                                Ui.Text.Class("text-xs text-zinc-800 dark:text-white")[stay.Rating]
                            ]
                        ]
                    ])
            ]
        ];

    private static IEnumerable<Component> Scenes(string carousel) =>
        SceneNames.Select(scene =>
            Ui.CarouselSlide.Key(carousel + "-" + scene).Class("w-full")[
                Div.Class(Box + " aspect-16/9 rounded-lg text-sm text-zinc-500 dark:text-white/50")[scene]
            ]);

    private static IEnumerable<Component> Labelled(string label) =>
        Enumerable.Range(0, 2).Select(slide =>
            Ui.CarouselSlide.Key(label + "-" + slide.ToString(CultureInfo.InvariantCulture)).Class("w-full")[
                Div.Class(Box + " h-40 w-full text-sm text-zinc-500 dark:text-white/50")[label]
            ]);

    private static IEnumerable<Component> Numbered(string carousel, int count) =>
        Enumerable.Range(1, count).Select(number =>
            Ui.CarouselSlide.Key(carousel + "-" + number.ToString(CultureInfo.InvariantCulture)).Class("w-4/5 sm:w-1/2 md:w-1/3")[
                Div.Class(Box + " aspect-2/1 rounded-lg text-2xl text-zinc-400 dark:text-white/40")[number.ToString(CultureInfo.InvariantCulture)]
            ]);

    private static Component Narrow(Component carousel) => Div.Class("mx-auto w-full max-w-sm")[carousel];

    private static Component Example(string name, string title, Component body) =>
        Div.Key(name).Data(Testid("ui-carousel-" + name)).Class("space-y-3")[
            H3.Class("text-sm font-medium text-ui-muted")[title],
            body
        ];

    private static AttrBag Testid(string value) => new("testid", value);
}
