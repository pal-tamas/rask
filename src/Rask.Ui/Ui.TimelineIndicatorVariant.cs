namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     How a <see cref="UiTimelineIndicator" /> is drawn: Flux's <c>variant</c>.
    /// </summary>
    public enum TimelineIndicatorVariant
    {
        /// <summary>A filled circle of the timeline's size. The default.</summary>
        Default = 0,

        /// <summary>No circle and no size of its own: whatever it holds, as large as that is.</summary>
        Bare,
    }
}
