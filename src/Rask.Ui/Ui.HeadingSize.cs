namespace Rask;

public static partial class Ui
{
    /// <summary>How big a <see cref="UiHeading" /> is drawn.</summary>
    public enum HeadingSize
    {
        /// <summary>14px. The default: an input's label, a toast's title.</summary>
        Base = 0,

        /// <summary>16px. A modal's or a card's heading.</summary>
        Lg,

        /// <summary>24px. Hero text, rarely.</summary>
        Xl,

        /// <summary>36px. Flux's <c>2xl</c>: a prominent page title.</summary>
        Xxl,
    }
}
