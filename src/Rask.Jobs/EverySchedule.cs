namespace Rask.Background;

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
