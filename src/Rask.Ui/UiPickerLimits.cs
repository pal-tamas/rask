namespace Rask;

/// <summary>
///     What a picker's field says of a value the reader typed outside its limits (#1273): a day before <c>Min</c>,
///     after <c>Max</c> or <c>Unavailable</c>, a range shorter than <c>MinRange</c> or longer than <c>MaxRange</c>, a
///     time outside its limits. Such a value is not taken — the model keeps what it held — and the form says the
///     sentence under the field until one within them arrives (<see cref="UiFormCommit" />'s unread write), as it says
///     what it cannot read of a number. Null for a value within every limit.
/// </summary>
internal static class UiPickerLimits
{
    /// <summary>What the field says of <paramref name="day" />, or null when the calendar offers it.</summary>
    internal static string? Day(DateOnly day, UiCalendarOptions options)
    {
        if (options.Min is { } min && day < min)
        {
            return RaskStrings.Get(RaskString.ValidationDateMin, "Choose a date on or after {0}.", Written(min, options));
        }

        if (options.Max is { } max && day > max)
        {
            return RaskStrings.Get(RaskString.ValidationDateMax, "Choose a date on or before {0}.", Written(max, options));
        }

        return options.Unavailable?.Contains(day) == true ? Unavailable(Written(day, options)) : null;
    }

    /// <summary>
    ///     What the field says of a range as long as <paramref name="range" />, or null when its length is allowed. A
    ///     range of one day is a range the calendar always allows, as <c>rask-calendar.ts</c> does: its start pressed
    ///     twice.
    /// </summary>
    internal static string? Length(UiDateRange range, UiCalendarOptions options)
    {
        if (range == default || range.Start == range.End)
        {
            return null;
        }

        if (options.MinRange is { } fewest && range.Count < fewest)
        {
            return RaskStrings.Get(RaskString.ValidationRangeMin, "Choose a range of at least {0} days.", fewest);
        }

        return options.MaxRange is { } most && range.Count > most
            ? RaskStrings.Get(RaskString.ValidationRangeMax, "Choose a range of at most {0} days.", most)
            : null;
    }

    /// <summary>What the field says of <paramref name="time" />, or null when it is within the limits and not unavailable.</summary>
    /// <param name="time">The time typed.</param>
    /// <param name="min">The earliest time, or null.</param>
    /// <param name="max">The latest time, or null.</param>
    /// <param name="unavailable">Whether the time is one that cannot be picked.</param>
    /// <param name="written">How the picker writes a time.</param>
    internal static string? Time(TimeOnly time, TimeOnly? min, TimeOnly? max, bool unavailable, Func<TimeOnly, string> written)
    {
        if (min is { } earliest && time < earliest)
        {
            return RaskStrings.Get(RaskString.ValidationTimeMin, "Choose a time at or after {0}.", written(earliest));
        }

        if (max is { } latest && time > latest)
        {
            return RaskStrings.Get(RaskString.ValidationTimeMax, "Choose a time at or before {0}.", written(latest));
        }

        return unavailable ? Unavailable(written(time)) : null;
    }

    private static string Unavailable(string written) =>
        RaskStrings.Get(RaskString.ValidationUnavailable, "{0} is not available.", written);

    private static string Written(DateOnly day, UiCalendarOptions options) => UiDatePickerText.Medium(day, options.Culture);
}
