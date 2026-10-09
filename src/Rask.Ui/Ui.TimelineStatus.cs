namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     How far along a <see cref="UiTimelineItem" /> is: Flux's <c>status</c>.
    /// </summary>
    public enum TimelineStatus
    {
        /// <summary>No status: the indicator as it is drawn anywhere. The default.</summary>
        Default = 0,

        /// <summary>Done: a filled indicator, and a dark line on to the next item.</summary>
        Complete,

        /// <summary>Where things stand: a dark ring.</summary>
        Current,

        /// <summary>Not reached: a faint ring, and the content dimmed.</summary>
        Incomplete,
    }
}
