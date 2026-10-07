namespace Rask;

public static partial class Ui
{
    /// <summary>What a calendar picks — Flux UI's <c>mode</c>.</summary>
    public enum CalendarMode
    {
        /// <summary>One day: a <c>DateOnly</c>.</summary>
#pragma warning disable CA1720 // Flux's own name for the mode
        Single = 0,
#pragma warning restore CA1720

        /// <summary>Several days: a collection of <c>DateOnly</c>.</summary>
        Multiple,

        /// <summary>A stretch of days: a <see cref="UiDateRange" />.</summary>
        Range,
    }
}
