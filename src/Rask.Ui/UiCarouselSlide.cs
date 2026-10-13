using System.Globalization;
using Rask.Core.Components;

namespace Rask;

/// <summary>
///     Flux's <c>flux:carousel.slide</c>: one slide of a <see cref="UiCarousel" />.
/// </summary>
/// <remarks>
///     Its width decides how many are in view: <c>.Class("w-full")</c> for one at a time,
///     <c>.Class("w-4/5 sm:w-1/2 md:w-1/3")</c> for a row of them. A screen reader hears "slide, 2 of 5".
/// </remarks>
public sealed partial class UiCarouselSlide : Component
{
    private static readonly UiPartMarker Marker = new("ui-carousel-slide");

    private int _place;
    private int _total;

    /// <summary>Classes for the call site: the slide's width, above all.</summary>
    public string? Class { get; set; }

    /// <summary>Says which slide this is of how many. The carousel's own chain step.</summary>
    internal void Placed(int place, int total)
    {
        if (_place == place && _total == total)
        {
            return;
        }

        (_place, _total) = (place, total);
        // What a generated step does when it writes a new value: the slide is drawn again with it.
        BuilderRuntime.MarkChanged(this);
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var slide = Div
            .Class("[:where(&)]:block shrink-0 snap-start", Class)
            .Role("group")
            // The first slide is the one in view when the carousel is first rendered; the runtime moves the mark.
            .Data(_place == 1 ? Marker.With(null, "selected", null) : Marker.With(null));
        var role = RaskStrings.Get(RaskString.CarouselSlideRole, "slide");

        return _place == 0
            ? slide.Aria("roledescription", role)[Children ?? []]
            : slide.Aria(("roledescription", role), ("label", Label()))[Children ?? []];
    }

    private string Label() => RaskStrings.Get(
        RaskString.CarouselSlideLabel,
        "{0} of {1}",
        _place.ToString(CultureInfo.InvariantCulture),
        _total.ToString(CultureInfo.InvariantCulture));
}
