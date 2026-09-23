namespace Rask.Jobs;

/// <summary>
/// When a recurring job is due. Either a plain interval (<c>.Every(1.Hour)</c>) or a calendar time in the
/// app's <see cref="JobsOptions.TimeZone"/> (<c>.Daily.At(3, 00)</c>, <c>.Weekly.On(DayOfWeek.Monday).At(9, 00)</c>).
/// </summary>
public abstract class Schedule
{
    private protected Schedule()
    {
    }

    /// <summary>How it reads back to an operator: "every 01:00:00", "daily at 03:00", "every Monday at 09:00".</summary>
    public abstract override string ToString();

    /// <summary>
    /// The tick to claim at <paramref name="now"/>, given the job's <paramref name="last"/> enqueue (UTC).
    /// <c>Due</c> is the watermark a claim must beat — a row whose <c>LastEnqueuedAt</c> is null or at most
    /// <c>Due</c> is owed a run — and <c>Next</c> is the value that stamps the claim.
    /// </summary>
    internal abstract (DateTime Due, DateTime Next) Tick(DateTime? last, DateTime now, TimeZoneInfo zone);
}

/// <summary>A job that runs every <paramref name="interval"/>, measured from its last run.</summary>
internal sealed class EverySchedule(TimeSpan interval) : Schedule
{
    internal TimeSpan Interval { get; } = interval;

    public override string ToString() => $"every {Compact(Interval)}";

    /// <summary>"1h 30m" — a cadence an operator reads at a glance, not "01:30:00".</summary>
    private static string Compact(TimeSpan span)
    {
        var parts = new List<string>(4);
        if (span.Days > 0)
        {
            parts.Add($"{span.Days}d");
        }

        if (span.Hours > 0)
        {
            parts.Add($"{span.Hours}h");
        }

        if (span.Minutes > 0)
        {
            parts.Add($"{span.Minutes}m");
        }

        if (span.Seconds > 0 || parts.Count == 0)
        {
            parts.Add($"{span.Seconds}s");
        }

        return string.Join(' ', parts);
    }

    internal override (DateTime Due, DateTime Next) Tick(DateTime? last, DateTime now, TimeZoneInfo zone)
    {
        // Anchor the next run to the schedule (last + interval), not the (late) poll time, so cadence
        // doesn't drift by a poll interval each cycle. If we fell more than one interval behind (e.g. the
        // app was down), reset to now so we don't burst a run of catch-up jobs.
        var next = last is { } prev && now - prev < Interval * 2 ? prev + Interval : now;
        return (now - Interval, next);
    }
}

/// <summary>How often a calendar schedule comes round.</summary>
public enum Cadence
{
    /// <summary>Once a day, at the given time.</summary>
    Daily,

    /// <summary>Once a week, on the given day.</summary>
    Weekly,

    /// <summary>Once a month, on the given date.</summary>
    Monthly,
}

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
