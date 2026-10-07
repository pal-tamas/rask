namespace Rask;

public static partial class Ui
{
    /// <summary>How big a <see cref="UiCardHeading" /> looks. Flux's <c>size</c>.</summary>
    public enum CardHeadingSize
    {
        /// <summary>The card's own title.</summary>
        Base = 0,

        /// <summary>A section heading above a card.</summary>
        Lg,

        /// <summary>A page's heading.</summary>
        Xl,
    }
}
