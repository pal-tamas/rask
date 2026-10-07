namespace Rask;

/// <summary>
/// Flux UI's <c>mode</c> on <c>Ui.Calendar</c>, as the step that opens it: <c>Ui.Calendar.Range.Bind(…)</c>,
/// <c>Ui.Calendar.Multiple.Values(days)</c>.
/// </summary>
/// <remarks>
/// The step hands back the entry of the calendar that binds that mode's type, so a range bound to a single day
/// does not compile. <c>Ui.Calendar</c> with no step is Flux's default: one day.
/// </remarks>
public static class UiCalendarModes
{
#pragma warning disable CA1822, S2325 // reached through the entry VALUE (Ui.Calendar.Range): a static member would hang off the seed's type name instead
#pragma warning disable CA1720 // "Single" is Flux's own name for the mode
    extension(RaskSeed_UiCalendar calendar)
    {
        /// <summary>One day — Flux's default, and what <c>Ui.Calendar</c> is without a mode step.</summary>
        public RaskSeed_UiCalendar Single => calendar;

        /// <summary>Several days: binds a collection of <c>DateOnly</c>.</summary>
        public RaskSeed_UiCalendarMultiple Multiple => default;

        /// <summary>A range of days: binds a <see cref="UiDateRange" />.</summary>
        public RaskSeed_UiCalendarRange Range => default;

        /// <summary>The mode as a value, for when it is decided at run time; what is bound has to agree with it.</summary>
        /// <param name="mode">Which of Flux's modes.</param>
        public UiCalendarModeEntry Mode(Ui.CalendarMode mode) => new(mode);
    }

#pragma warning restore CA1720
#pragma warning restore CA1822, S2325

    internal static void Check(Ui.CalendarMode? asked, Ui.CalendarMode bound)
    {
        if (asked is { } mode && mode != bound)
        {
            throw new InvalidOperationException(
                $"Ui.Calendar is in {mode} mode but what it binds is {bound}'s "
                + $"({What(bound)}). Open it with Ui.Calendar.{mode} and bind {What(mode)}.");
        }
    }

    private static string What(Ui.CalendarMode mode) => mode switch
    {
        Ui.CalendarMode.Multiple => "a collection of DateOnly",
        Ui.CalendarMode.Range => "a UiDateRange",
        _ => "a DateOnly",
    };
}
