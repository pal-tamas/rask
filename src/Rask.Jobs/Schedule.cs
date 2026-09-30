namespace Rask.Background;

/// <summary>
/// When a recurring job is due. Either a plain interval (<c>.Every(1.Hour)</c>) or a calendar time in the
/// app's <see cref="JobsOptions.TimeZone"/> (<c>.Daily.At(3, 0)</c>, <c>.Weekly.On(DayOfWeek.Monday).At(9, 0)</c>).
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
