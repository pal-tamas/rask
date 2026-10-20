using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's carousel as a C# component: each example with its own code, and its reference.</summary>
[Route("carousel")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiCarouselPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Carousel.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Carousel)
            .Lead("Slides that scroll sideways and come to rest on one. The browser scrolls and snaps, so a wheel, a "
                  + "swipe and the keyboard all move it, and the arrows, the indicators and autoplay follow in the "
                  + "browser with nothing sent to the page. A slide's width is its class.")
            .Api(UiApi.Carousel)[
            UiDocsExample.Key("ui-carousel-basic").Heading("Carousel").File("CarouselExample.cs").Demo(CarouselExample).Testid("ui-carousel-basic")
                .Blurb("A slide at a time, with arrows over it."),
            UiDocsExample.Key("ui-carousel-indicators").Heading("Indicators").File("CarouselIndicatorsExample.cs").Demo(CarouselIndicatorsExample).Testid("ui-carousel-indicators")
                .Blurb("One button per slide under the carousel, and no arrows."),
            UiDocsExample.Key("ui-carousel-autoplay").Heading("Autoplay").File("CarouselAutoplayExample.cs").Demo(CarouselAutoplayExample).Testid("ui-carousel-autoplay")
                .Blurb("Moves on every 3.5 seconds, and holds under the pointer."),
            UiDocsExample.Key("ui-carousel-external").Heading("External controls").File("CarouselExternalControlsExample.cs").Demo(CarouselExternalControlsExample).Testid("ui-carousel-external")
                .Blurb("Ui.CarouselControls anywhere on the page move the carousel that carries their name."),
            UiDocsExample.Key("ui-carousel-position").Heading("Arrow position").File("CarouselArrowPositionExample.cs").Demo(CarouselArrowPositionExample).Testid("ui-carousel-position")
                .Blurb("Inside, the default, or half over each edge."),
            UiDocsExample.Key("ui-carousel-fade").Heading("Fade").File("CarouselFadeExample.cs").Demo(CarouselFadeExample).Testid("ui-carousel-fade")
                .Blurb("The edge with more slides behind it fades."),
            UiDocsExample.Key("ui-carousel-page").Heading("Page advance").File("CarouselPageExample.cs").Demo(CarouselPageExample).Testid("ui-carousel-page")
                .Blurb("An arrow moves by the slides in view."),
            UiDocsExample.Key("ui-carousel-outside").Heading("Arrows outside").File("CarouselOutsideExample.cs").Demo(CarouselOutsideExample).Testid("ui-carousel-outside"),
            UiDocsExample.Key("ui-carousel-rewind").Heading("Rewind").File("CarouselRewindExample.cs").Demo(CarouselRewindExample).Testid("ui-carousel-rewind")
                .Blurb("At the end the next arrow stays, and goes back to the first slide."),
            UiDocsExample.Key("ui-carousel-disabled").Heading("Disabled").File("CarouselDisabledExample.cs").Demo(CarouselDisabledExample).Testid("ui-carousel-disabled")
                .Blurb("The arrows and the indicators are out of use; the slides still scroll."),
            UiCarouselBehaviour
        ];
}
