namespace Rask.Background;

/// <summary>A monthly job still being worded: <c>.Monthly.On(1).At(6, 00)</c>.</summary>
/// <param name="job">The job being scheduled.</param>
public readonly struct MonthlyJob(RecurringJob job)
{
    /// <summary>
    /// Runs it on the <paramref name="day"/>th of each month. A month too short for it runs on its last
    /// day, so <c>On(31)</c> never skips February.
    /// </summary>
    public MonthlyDayJob On(int day)
    {
        if (day is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(day), day, "A day of the month is between 1 and 31.");
        }

        return new MonthlyDayJob(job, day);
    }
}
