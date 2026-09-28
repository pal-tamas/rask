namespace Rask.Background;

/// <summary>A monthly job with its date chosen: <c>.At(6, 00)</c> finishes it.</summary>
/// <param name="job">The job being scheduled.</param>
/// <param name="day">The day of the month it runs on.</param>
public readonly struct MonthlyDayJob(RecurringJob job, int day)
{
    /// <summary>Runs it at <paramref name="hour"/>:<paramref name="minute"/>, in the app's time zone.</summary>
    public RecurringJob At(int hour, int minute = 0) => At(Schedules.Time(hour, minute));

    /// <summary>Runs it at <paramref name="time"/>, in the app's time zone.</summary>
    public RecurringJob At(TimeOnly time) => job.With(new CalendarSchedule(Cadence.Monthly, time, null, day));
}
