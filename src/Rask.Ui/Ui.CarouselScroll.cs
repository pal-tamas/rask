namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiCarousel" /> moves when an arrow or an indicator sends it somewhere.</summary>
    public enum CarouselScroll
    {
        /// <summary>It glides. Flux's default.</summary>
        Smooth,

        /// <summary>It jumps.</summary>
        Instant,
    }
}
