using System.Globalization;

namespace Rask;

/// <summary>Flux's calendar props, resolved: what a calendar or a date picker hands the grid.</summary>
internal sealed record UiCalendarOptions(
    DateOnly Today,
    CultureInfo Culture,
    DayOfWeek StartDay,
    Ui.CalendarSize Size,
    int Months)
{
    internal DateOnly? Min { get; init; }

    internal DateOnly? Max { get; init; }

    internal IReadOnlyCollection<DateOnly>? Unavailable { get; init; }

    internal int? MinRange { get; init; }

    internal int? MaxRange { get; init; }

    internal DateOnly? OpenTo { get; init; }

    internal bool ForceOpenTo { get; init; }

    internal bool Navigation { get; init; } = true;

    internal bool Static { get; init; }

    internal bool WeekNumbers { get; init; }

    internal bool SelectableHeader { get; init; }

    internal bool WithToday { get; init; }

    internal bool FixedWeeks { get; init; }

    internal string? Class { get; init; }

    internal string? Id { get; init; }

    internal IReadOnlyDictionary<string, string?>? Data { get; init; }

    /// <summary>
    ///     The grid sits in a popup: the day Tab would land on asks for the focus when the popup opens, so it
    ///     does not fall on the first control in the header.
    /// </summary>
    internal bool InPopup { get; init; }

    /// <summary>The popover a pick that finishes the choice closes, when the grid sits in a date picker.</summary>
    internal string? Closes { get; init; }

    internal static DateOnly Now() => DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().Date);
}
