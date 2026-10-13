namespace Rask;

public static partial class Ui
{
    /// <summary>How far an arrow moves a <see cref="UiCarousel" />.</summary>
    public enum CarouselAdvance
    {
        /// <summary>One slide. Flux's default.</summary>
        Slide,

        /// <summary>As many slides as are fully in view.</summary>
        Page,
    }
}
