namespace Rask.Background;

/// <summary>A weekly job with its day chosen: <c>.At(9, 00)</c> finishes it.</summary>
/// <param name="job">The job being scheduled.</param>
/// <param name="day">The weekday it runs on.</param>
public readonly struct WeeklyDayJob(RecurringJob job, DayOfWeek day)
{
    /// <summary>Runs it at <paramref name="hour"/>:<paramref name="minute"/>, in the app's time zone.</summary>
    public RecurringJob At(int hour, int minute = 0) => At(Schedules.Time(hour, minute));

    /// <summary>Runs it at <paramref name="time"/>, in the app's time zone.</summary>
    public RecurringJob At(TimeOnly time) => job.With(new CalendarSchedule(Cadence.Weekly, time, day, null));
}
