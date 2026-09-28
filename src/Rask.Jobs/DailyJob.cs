namespace Rask.Background;

/// <summary>A daily job still being worded: <c>.Daily.At(3, 00)</c>.</summary>
/// <param name="job">The job being scheduled.</param>
public readonly struct DailyJob(RecurringJob job)
{
    /// <summary>Runs it at <paramref name="hour"/>:<paramref name="minute"/>, in the app's time zone.</summary>
    public RecurringJob At(int hour, int minute = 0) => At(Schedules.Time(hour, minute));

    /// <summary>Runs it at <paramref name="time"/>, in the app's time zone.</summary>
    public RecurringJob At(TimeOnly time) => job.With(new CalendarSchedule(Cadence.Daily, time, null, null));
}
