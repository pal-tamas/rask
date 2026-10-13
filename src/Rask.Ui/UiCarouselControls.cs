namespace Rask;

/// <summary>
///     Flux's <c>flux:carousel.controls</c>: the previous and next buttons of a <see cref="UiCarousel" />,
///     placed anywhere on the page — beside the heading above it, say.
/// </summary>
/// <remarks>
///     <c>Ui.CarouselControls.Name("popular-stays")</c> beside <c>Ui.Carousel.Name("popular-stays").Arrows(false)</c>.
///     At the end a button points to, it dims instead of going away, so the pair keeps its place.
/// </remarks>
public sealed partial class UiCarouselControls : Component
{
    private static readonly UiPartMarker Marker = new UiPartMarker("ui-carousel-controls").And("rask-carousel-controls");

    /// <summary>The <see cref="UiCarousel.Name" /> of the carousel these buttons move.</summary>
    public string? Name { get; set; }

    /// <summary>Classes for the call site, added to the controls' own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("[:where(&)]:flex [:where(&)]:gap-2", Class).Data(Name is null ? Marker.With(null) : Marker.With(null, "name", Name))[
            UiCarouselArrow.Beside(next: false),
            UiCarouselArrow.Beside(next: true)
        ];
}
