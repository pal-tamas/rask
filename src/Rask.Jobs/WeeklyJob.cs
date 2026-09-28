namespace Rask.Background;

/// <summary>A weekly job still being worded: <c>.Weekly.On(DayOfWeek.Monday).At(9, 00)</c>.</summary>
/// <param name="job">The job being scheduled.</param>
public readonly struct WeeklyJob(RecurringJob job)
{
    /// <summary>Runs it on <paramref name="day"/>, at the time the next step gives.</summary>
    public WeeklyDayJob On(DayOfWeek day) => new(job, day);
}
