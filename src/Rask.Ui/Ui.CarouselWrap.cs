namespace Rask;

public static partial class Ui
{
    /// <summary>What advancing past the last slide of a <see cref="UiCarousel" /> does.</summary>
    public enum CarouselWrap
    {
        /// <summary>Nothing: the next arrow goes away at the end. Flux's default.</summary>
        None,

        /// <summary>It returns to the first slide, and the next arrow stays.</summary>
        Rewind,
    }
}
