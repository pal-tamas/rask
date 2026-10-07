namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     How large a <see cref="UiTimeline" />'s indicators are: Flux's <c>size</c>.
    /// </summary>
    public enum TimelineSize
    {
        /// <summary>32px indicators on a 1px line. The default.</summary>
        Base = 0,

        /// <summary>48px indicators, for numbered steps; on a whole timeline, a 2px line and wider gaps too.</summary>
        Lg,
    }
}
