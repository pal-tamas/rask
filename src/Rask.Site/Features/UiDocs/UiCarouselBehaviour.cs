namespace Rask.Site.Features.UiDocs;

/// <summary>How a carousel behaves: what the browser and the runtime each do, autoplay, and what a screen reader hears.</summary>
internal sealed partial class UiCarouselBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Browser(),
            Autoplay(),
            Announced()
        ];

    private static UiDocsTopic Browser() =>
        UiDocsTopic.Key("browser").Heading("Nothing is sent to the page").Paragraphs([
            "The scrolling is the browser's: scroll-snap, so a wheel, a swipe and the keyboard move it as they move any "
            + "scroller. What a stylesheet cannot do, Rask's runtime does from the markup (`data-rask-carousel`): it "
            + "marks the root and each arrow `data-at-start` and `data-at-end`, disables the arrow with nowhere to go, "
            + "marks the slide in view `data-selected` and its indicator `aria-current`, moves the track when an arrow "
            + "or an indicator is pressed, and runs autoplay. There is no `OnChange`, as Flux has none.",
            "A slide's width is its class, and decides how many are in view. daisyUI's `carousel` wrapper, which "
            + "`Ui.Carousel` used to draw around `carousel-item` children, is gone.",
        ]);

    private static UiDocsTopic Autoplay() =>
        UiDocsTopic.Key("autoplay").Heading("Autoplay").Paragraphs([
            "`Autoplay()` advances on its interval, 5000ms unless `AutoplayInterval` says otherwise, and goes back to "
            + "the first slide after the last. It holds under the pointer and while focus is inside, and never starts "
            + "for a reader who asked for less motion.",
        ]);

    private static UiDocsTopic Announced() =>
        UiDocsTopic.Key("announced").Heading("What a screen reader hears").Paragraphs([
            "A group called \"carousel\" holding \"slide, 2 of 5\". Those words, and the buttons' names, are in the "
            + "translation catalog.",
        ]);
}
