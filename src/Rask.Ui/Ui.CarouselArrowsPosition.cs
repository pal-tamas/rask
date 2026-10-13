namespace Rask;

public static partial class Ui
{
    /// <summary>Where a <see cref="UiCarousel" />'s own arrows sit against its viewport.</summary>
    public enum CarouselArrowsPosition
    {
        /// <summary>Over the slides, 12px in from each edge. Flux's default.</summary>
        Inside,

        /// <summary>Centred on each edge, half over the slides.</summary>
        Overlap,

        /// <summary>Beside the viewport, clear of the slides.</summary>
        Outside,
    }
}
