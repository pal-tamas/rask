using System.Globalization;

namespace Rask;

/// <summary>
///     Flux's <c>flux:carousel</c>: a row of <see cref="UiCarouselSlide" />s that scrolls sideways and comes
///     to rest on one, with arrows over it and, if asked, an indicator per slide under it.
/// </summary>
/// <remarks>
///     <para>
///     <c>Ui.Carousel[Ui.CarouselSlide.Class("w-full")[…], …]</c>. The scrolling is the browser's own —
///     scroll-snap, so a wheel, a swipe and the keyboard move it as they move any scroller — and
///     <see cref="Fade" /> follows it in the stylesheet.
///     </para>
///     <para>
///     It is rendered as a carousel at its start is: first slide selected, previous arrow gone. From there
///     Rask's runtime keeps it (<c>data-rask-carousel</c>): where it stands, the arrows, the indicators, a
///     <see cref="UiCarouselControls" /> elsewhere on the page, and <see cref="Autoplay" /> — all in the
///     browser, with nothing sent to the page.
///     </para>
/// </remarks>
public sealed partial class UiCarousel : Component
{
    private const string Track =
        "flex [:where(&)]:gap-4 overflow-x-auto overflow-y-hidden overscroll-x-contain snap-x [scrollbar-width:none]";

    private const string Dot =
        "block size-2 rounded-full bg-zinc-300 transition-colors group-hover:bg-zinc-400 group-data-selected:bg-zinc-800"
        + " dark:bg-white/30 dark:group-hover:bg-white/50 dark:group-data-selected:bg-white";

    private static readonly Dictionary<string, string?> ViewportMark = new(StringComparer.Ordinal) { ["ui-carousel-viewport"] = null };

    private static readonly Dictionary<string, string?> TrackMark = new(StringComparer.Ordinal)
    {
        ["ui-carousel-track"] = null,
        ["rask-carousel-track"] = null,
    };

    private static readonly Dictionary<string, string?> FadingTrackMark = new(TrackMark, StringComparer.Ordinal) { ["fade"] = null };

    private static readonly Dictionary<string, string?> DotsMark = new(StringComparer.Ordinal) { ["rask-carousel-indicators"] = null };

    private static readonly Dictionary<string, string?> SelectedDot = new(StringComparer.Ordinal) { ["selected"] = null };

    /// <summary>Whether it has its own previous and next arrows. They are there when unset.</summary>
    public bool? Arrows { get; set; }

    /// <summary>Where those arrows sit. <see cref="Ui.CarouselArrowsPosition.Inside" /> when unset.</summary>
    public Ui.CarouselArrowsPosition? ArrowsPosition { get; set; }

    /// <summary>Classes added to each of those arrows.</summary>
    public string? ArrowsClass { get; set; }

    /// <summary>Advances by itself, pausing while it is hovered, focused or being moved by hand.</summary>
    public bool? Autoplay { get; set; }

    /// <summary>Milliseconds between two advances of <see cref="Autoplay" />. 5000 when unset.</summary>
    public int? AutoplayInterval { get; set; }

    /// <summary>Shows one indicator per slide under the carousel, each of which goes to its slide.</summary>
    public bool? Indicators { get; set; }

    /// <summary>Takes the arrows and the indicators out of use. The slides still scroll.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Fades the edge of the track that has more slides behind it.</summary>
    public bool? Fade { get; set; }

    /// <summary>
    ///     Runs the track through the padding of what holds it, the slides still starting at the gutter:
    ///     by <c>--ui-bleed</c>, which a <see cref="UiCard" /> sets, or 1.5rem.
    /// </summary>
    public bool? Bleed { get; set; }

    /// <summary>How firmly it rests on a slide. <see cref="Ui.CarouselSnap.Proximity" /> when unset.</summary>
    public Ui.CarouselSnap? Snap { get; set; }

    /// <summary>How an arrow or an indicator moves it. <see cref="Ui.CarouselScroll.Smooth" /> when unset.</summary>
    public Ui.CarouselScroll? Scroll { get; set; }

    /// <summary>How far an arrow moves it. <see cref="Ui.CarouselAdvance.Slide" /> when unset.</summary>
    public Ui.CarouselAdvance? Advance { get; set; }

    /// <summary>What advancing past the last slide does. <see cref="Ui.CarouselWrap.None" /> when unset.</summary>
    public Ui.CarouselWrap? Wrap { get; set; }

    /// <summary>A name unique on the page, for a <see cref="UiCarouselControls" /> elsewhere to move it by.</summary>
    public string? Name { get; set; }

    /// <summary>Classes added to the scrolling track.</summary>
    public string? TrackClass { get; set; }

    /// <summary>Classes for the call site, added to the carousel's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var slides = Number();
        var bleed = Bleed == true;

        return Div
            .Class(UiClass.Compose(bleed ? "[:where(&)]:block [:where(&)]:-mx-[var(--ui-bleed,1.5rem)]" : "[:where(&)]:block", Class))
            .Data(Settings())
            .Role("group")
            .Aria(
                ("roledescription", RaskStrings.Get(RaskString.CarouselRole, "carousel")),
                ("label", RaskStrings.Get(RaskString.CarouselLabel, "Items")))[
            Div.Class("relative").Data(ViewportMark)[
                Div.Class(UiClass.Compose(Track, Motion(), bleed ? "px-[var(--ui-bleed,1.5rem)] scroll-px-[var(--ui-bleed,1.5rem)]" : "", TrackClass))
                    .Data(Fade == true ? FadingTrackMark : TrackMark)
                    .Aria(("live", "polite"), ("atomic", "false"))[
                    Children ?? []
                ],
                Arrows == false ? null : UiCarouselArrow.Over(next: false, Position(next: false), ArrowsClass, Disabled == true),
                Arrows == false ? null : UiCarouselArrow.Over(next: true, Position(next: true), ArrowsClass, Disabled == true)
            ],
            Indicators == true ? Dots(slides) : null
        ];
    }

    // Tells each slide which one it is: a screen reader hears "slide, 2 of 5".
    private int Number()
    {
        var slides = Children?.OfType<UiCarouselSlide>().ToList() ?? [];
        for (var at = 0; at < slides.Count; at++)
        {
            slides[at].Placed(at + 1, slides.Count);
        }

        return slides.Count;
    }

    private Component Dots(int slides) =>
        Div.Class("mt-6 flex items-center justify-center transition-opacity duration-200")
            .Data(DotsMark)
            .Role("group")
            .Aria("label", RaskStrings.Get(RaskString.CarouselIndicators, "Choose slide to display"))[
            Enumerable.Range(1, slides).Select(slide =>
            {
                var dot = Button
                    .Key(slide)
                    .Type(ButtonType.Button)
                    .Disabled(Disabled == true)
                    .Class("group flex items-center justify-center p-1");
                var label = RaskStrings.Get(RaskString.CarouselShowSlide, "Show slide {0}", slide.ToString(CultureInfo.InvariantCulture));

                // Rendered at the start, where the first slide is the one in view; the runtime moves the mark.
                return slide == 1
                    ? dot.Data(SelectedDot).Aria(("label", label), ("current", "true"))[Span.Class(Dot)]
                    : dot.Aria("label", label)[Span.Class(Dot)];
            })
        ];

    private string Motion() => (Snap, Scroll) switch
    {
        (Ui.CarouselSnap.Mandatory, Ui.CarouselScroll.Instant) => "snap-mandatory scroll-auto",
        (Ui.CarouselSnap.Mandatory, _) => "snap-mandatory scroll-smooth",
        (_, Ui.CarouselScroll.Instant) => "snap-proximity scroll-auto",
        _ => "snap-proximity scroll-smooth",
    };

    private string Position(bool next) => (ArrowsPosition, next) switch
    {
        (Ui.CarouselArrowsPosition.Overlap, false) => "start-0 -translate-x-1/2",
        (Ui.CarouselArrowsPosition.Overlap, true) => "end-0 translate-x-1/2",
        (Ui.CarouselArrowsPosition.Outside, false) => "end-full me-3",
        (Ui.CarouselArrowsPosition.Outside, true) => "start-full ms-3",
        (_, false) => "start-3",
        (_, true) => "end-3",
    };

    // Flux's marker and the runtime's, the state a carousel starts in, and each setting that is not the default.
    private Dictionary<string, string?> Settings()
    {
        var data = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ui-carousel"] = null,
            ["rask-carousel"] = null,
            ["at-start"] = null,
        };

        if (Name is { } name)
        {
            data["name"] = name;
        }

        if (Advance == Ui.CarouselAdvance.Page)
        {
            data["advance"] = "page";
        }

        if (Wrap == Ui.CarouselWrap.Rewind)
        {
            data["wrap"] = "rewind";
        }

        if (Scroll == Ui.CarouselScroll.Instant)
        {
            data["scroll"] = "instant";
        }

        if (Autoplay == true)
        {
            data["autoplay"] = (AutoplayInterval ?? 5000).ToString(CultureInfo.InvariantCulture);
        }

        return data;
    }
}
