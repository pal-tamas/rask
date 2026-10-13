namespace Rask;

public static partial class Ui
{
    /// <summary>How firmly a <see cref="UiCarousel" /> comes to rest on a slide.</summary>
    public enum CarouselSnap
    {
        /// <summary>Only when it stops near one. Flux's default.</summary>
        Proximity,

        /// <summary>Always.</summary>
        Mandatory,
    }
}
