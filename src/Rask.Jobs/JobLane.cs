namespace Rask.Background;

/// <summary>
///     A lane at one moment, as <c>await Jobs.Lane(pump.Id)</c> reads it: what runs in it now, if anything, and what waits, in
///     the order the lane will take it.
/// </summary>
/// <param name="Name">The lane's name — what <c>.InLane(…)</c> was given.</param>
/// <param name="Running">The job running in it now, or null.</param>
/// <param name="Waiting">
///     The jobs that will still run in it, in the order it takes them — a job waiting out a retry's backoff included, with its
///     <see cref="LaneJob.Due" /> ahead. A dead letter, a processed job and a stopped one are not here: none of them will run.
/// </param>
public sealed record JobLane(string Name, LaneJob? Running, IReadOnlyList<LaneJob> Waiting)
{
    /// <summary>Nothing runs in the lane and nothing waits.</summary>
    public bool IsIdle => Running is null && Waiting.Count == 0;
}
