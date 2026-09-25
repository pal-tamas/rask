namespace Rask.Background;

/// <summary>A job that runs at a wall-clock time in the app's time zone, so it follows daylight saving.</summary>
internal sealed class CalendarSchedule(Cadence cadence, TimeOnly time, DayOfWeek? weekday, int? day) : Schedule
{
    internal Cadence Cadence { get; } = cadence;

    internal TimeOnly Time { get; } = time;

    internal DayOfWeek? Weekday { get; } = weekday;

    internal int? Day { get; } = day;

    public override string ToString() => Cadence switch
    {
        Cadence.Weekly => $"every {Weekday} at {Time:HH\\:mm}",
        Cadence.Monthly => $"on day {Day} of each month at {Time:HH\\:mm}",
        _ => $"daily at {Time:HH\\:mm}",
    };

    internal override (DateTime Due, DateTime Next) Tick(DateTime? last, DateTime now, TimeZoneInfo zone)
    {
        var occurrence = LastOccurrenceBefore(now, zone);

        // The claim stamps the occurrence itself, so the same tick can never be owed twice: a row already
        // stamped with it is not "at most one tick before it". Measuring from `now` instead would re-arm the
        // job on the very next poll, which is how a nightly backup turns into one every five seconds.
        return (occurrence.AddTicks(-1), occurrence);
    }

    /// <summary>The most recent scheduled instant at or before <paramref name="now"/>, as UTC.</summary>
    private DateTime LastOccurrenceBefore(DateTime now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(now, zone);
        var candidate = local.Date + Time.ToTimeSpan();

        switch (Cadence)
        {
            case Cadence.Weekly:
                // Walk back to the scheduled weekday; if today *is* it but the time hasn't come, that was
                // last week's.
                var back = ((int)local.DayOfWeek - (int)Weekday!.Value + 7) % 7;
                candidate = candidate.AddDays(-back);
                if (candidate > local)
                {
                    candidate = candidate.AddDays(-7);
                }

                break;

            case Cadence.Monthly:
                // A month shorter than the chosen date runs on its last day, so "the 31st" never skips
                // February. Clamping is what an operator means by "month end".
                candidate = OnDay(local.Year, local.Month, Day!.Value);
                if (candidate > local)
                {
                    var previous = local.AddMonths(-1);
                    candidate = OnDay(previous.Year, previous.Month, Day.Value);
                }

                break;

            default:
                if (candidate > local)
                {
                    candidate = candidate.AddDays(-1);
                }

                break;
        }

        // A time that daylight saving skipped never exists locally; the conversion below would throw, so the
        // job runs at the moment the clocks jump to instead of being silently missed for a year.
        if (zone.IsInvalidTime(candidate))
        {
            candidate = candidate.Add(zone.GetAdjustmentRules().Length > 0 ? TimeSpan.FromHours(1) : TimeSpan.Zero);
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), zone);
    }

    private DateTime OnDay(int year, int month, int day) =>
        new DateTime(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)), 0, 0, 0, DateTimeKind.Unspecified)
        + Time.ToTimeSpan();
}
