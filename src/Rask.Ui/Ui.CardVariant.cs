namespace Rask;

public static partial class Ui
{
    /// <summary>The surface a <see cref="UiCard" /> is drawn on. Flux's <c>variant</c>.</summary>
    public enum CardVariant
    {
        /// <summary>Raised: white with a shadow in light, lifted off the page in dark.</summary>
        Default = 0,

        /// <summary>A quieter tint, for secondary panels.</summary>
        Muted,

        /// <summary>The faintest tint, for light grouping.</summary>
        Soft,

        /// <summary>Just an edge, over whatever is behind it.</summary>
        Outline,

        /// <summary>A tint with no edge, for inline callouts.</summary>
        Filled,
    }
}
