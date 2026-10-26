using System.Globalization;
using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/carousel</c>, example for example, each as it is first painted: at its
///     start, first slide selected, previous arrow gone.
/// </summary>
/// <remarks>
///     What a slide holds on that page — photographs, a quotation, a numbered placeholder — is the app's,
///     with the app's own classes, and is written here as an app would write it. The photographs are
///     Flux's docs site's own, linked and not copied. "Arrow position" shows two of its three values:
///     Flux's page does not draw <c>outside</c>.
/// </remarks>
public sealed partial class CarouselParity : FluxParity
{
    private const string Images = "https://fluxui.dev/img/carousel/";

    // What an app's own Tailwind build emits for the classes these examples hand to Class.
    private const string AppUtilities =
        "<style>.parity-full{width:100%}.parity-third{width:80%}@media (width >= 40rem){.parity-third{width:50%}}@media (width >= 48rem){.parity-third{width:33.3333%}}.parity-below{margin-bottom:24px}.parity-above{margin-top:8px}"
        // `rounded-lg overflow-hidden` on a track rounds it and no more: the track's own overflow-x wins.
        + ".parity-round{border-radius:8px}"
        + ".parity-photo{aspect-ratio:16/9;border-radius:8px;object-fit:cover}"
        + ".parity-house{aspect-ratio:2/1;border-radius:8px;object-fit:cover}"
        + ".parity-quote{display:flex;flex-direction:column;align-items:center;gap:16px;text-align:center}"
        + ".parity-face{width:56px;height:56px;border-radius:calc(infinity * 1px);aspect-ratio:1/1;object-fit:cover}"
        + ".parity-said{text-wrap:balance;color:oklch(37% .013 285.805)}.dark .parity-said{color:oklch(87.1% .006 286.286)}"
        + ".parity-who{font-size:14px;line-height:20px;color:oklch(55.2% .016 285.938)}"
        + ".parity-name{font-weight:600;color:oklch(37% .013 285.805)}.dark .parity-name{color:oklch(92% .004 286.32)}"
        + ".parity-xs{font-size:12px;line-height:16px}.parity-ink{color:oklch(27.4% .006 286.033)}.dark .parity-ink{color:#fff}"
        + ".parity-star{width:12px;height:12px}"
        + ".parity-box{display:flex;align-items:center;justify-content:center;font-weight:500;background:oklch(96.7% .001 286.375)}"
        + ".dark .parity-box{background:color-mix(in oklab,#fff 10%,transparent)}.dark .parity-label{color:color-mix(in oklab,#fff 50%,transparent)}"
        + ".dark .parity-number{color:color-mix(in oklab,#fff 40%,transparent)}"
        + ".parity-label{aspect-ratio:2/1;width:100%;font-size:14px;line-height:20px;color:oklch(55.2% .016 285.938)}"
        + ".parity-number{aspect-ratio:2/1;border-radius:8px;font-size:24px;line-height:32px;color:oklch(70.5% .015 286.067)}</style>";

    private static readonly (string Face, string Said, string Name, string Role)[] Quotes =
    [
        ("headshot1.png", "“Flux made it possible to ship a polished UI in days, not weeks. Genuinely the most fun I’ve had building an app.”", "Graham Whitaker", " · Founder, Northwind"),
        ("headshot2.png", "“When I reach for a Flux component, it just works. The defaults are obvious. A real joy to use.”", "Elise Rowan", " · Engineering Lead, Southline Studio"),
        ("headshot3.png", "“I stopped writing UI primitives entirely. Flux + Livewire is the closest thing to a content-first dev experience for Laravel.”", "Jonah Vale", " · CTO, Alder & Co."),
    ];

    private static readonly (string Photo, string Name, string Price, string Rating)[] Stays =
    [
        ("house1.png", "Cottage in Mendocino", "$680", "5.0"),
        ("house5.png", "Lodge in the Thousand Islands", "$370", "4.96"),
        ("house6.png", "House in the Adirondacks", "$550", "4.88"),
        ("house4.png", "House in Cape Cod", "$840", "5"),
    ];

    public override string Page => "carousel";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Frame(
            "width:100%;max-width:384px",
            Raw.Value(AppUtilities),
            Ui.Carousel.TrackClass("parity-round")[Photos()]));

        yield return ("indicators", Frame(
            "width:100%;max-width:384px",
            Ui.Carousel.Class("parity-full").Indicators().Arrows(false)[
                Quotes.Select(quote =>
                    Ui.CarouselSlide.Key(quote.Name).Class("parity-full")[
                        Div.Class("parity-quote")[
                            Img.Src(Images + quote.Face).Alt("").Class("parity-face"),
                            Blockquote.Class("parity-said")[quote.Said],
                            Div.Class("parity-who")[Span.Class("parity-name")[quote.Name], quote.Role]
                        ]
                    ])
            ]));

        yield return ("autoplay", Frame(
            "width:100%;max-width:384px",
            Ui.Carousel.Autoplay().AutoplayInterval(3500).Indicators().TrackClass("parity-round")[Photos()]));

        yield return ("external-controls", Frame(
            "width:100%",
            Div.Style("display:flex;justify-content:space-between;align-items:center;margin-bottom:16px")[
                Div[Ui.Heading.Lg["Popular stays"], Ui.Text["Handpicked homes in beautiful places"]],
                Div[Ui.CarouselControls.Name("popular-stays")]
            ],
            Ui.Carousel.Name("popular-stays").Bleed().Arrows(false)[
                Stays.Select(stay =>
                    Ui.CarouselSlide.Key(stay.Name).Class("parity-third")[
                        Img.Src(Images + stay.Photo).Alt("").Class("parity-house"),
                        Ui.Heading.Class("parity-above")[stay.Name],
                        Div.Style("margin-top:4px;display:flex;gap:2px;align-items:center")[
                            Ui.Text.Class("parity-xs")[Ui.Link.Href("#").Ghost[stay.Price], " for 2 nights ・"],
                            Div.Style("display:flex;align-items:center;gap:2px")[
                                Ui.Icon.Name(Ui.IconName.Star).Variant(Ui.IconVariant.Micro).Class("parity-star"),
                                Ui.Text.Class("parity-xs parity-ink")[stay.Rating]
                            ]
                        ]
                    ])
            ]));

        yield return ("arrow-position", Frame(
            "width:100%;max-width:384px",
            Div.Style("max-width:320px;margin:0 auto")[
                Ui.Carousel.Class("parity-below").TrackClass("parity-round")[Labelled("Inside")],
                Ui.Carousel.Overlap.TrackClass("parity-round")[Labelled("Overlap")]
            ]));

        yield return ("fade", Frame("width:100%", Ui.Carousel.Fade().Bleed()[Numbered(5)]));

        yield return ("page-advance", Frame("width:100%", Ui.Carousel.Page.Bleed()[Numbered(10)]));
    }

    private static IEnumerable<Component> Photos() =>
        new[] { "seascape.png", "woodscape.png", "landscape.png" }.Select(photo =>
            (Component)Ui.CarouselSlide.Key(photo).Class("parity-full")[Img.Src(Images + photo).Alt("").Class("parity-photo")]);

    private static IEnumerable<Component> Labelled(string label) =>
        Enumerable.Range(0, 2).Select(slide =>
            (Component)Ui.CarouselSlide.Key(slide).Class("parity-full")[Div.Class("parity-box parity-label")[label]]);

    private static IEnumerable<Component> Numbered(int count) =>
        Enumerable.Range(1, count).Select(number =>
            (Component)Ui.CarouselSlide.Key(number).Class("parity-third")[
                Div.Class("parity-box parity-number")[number.ToString(CultureInfo.InvariantCulture)]
            ]);

    // The column an example has on Flux's page, with what holds the carousel centred in it.
    private static Component Frame(string holder, params Component[] items) =>
        Div.Style("display:flex;justify-content:center")[Div.Style(holder)[items]];
}
