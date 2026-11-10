namespace Rask.Background;

/// <summary>One job of a lane (<see cref="JobLane" />), as its row holds it.</summary>
/// <param name="Id">The job's row id: what <c>Jobs.Stop(id)</c> takes.</param>
/// <param name="Job">The job itself, or null when its payload no longer reads as a registered job — it is still listed, so it is seen.</param>
/// <param name="Type">The job's registered type name (<see cref="Background.Job.Type" />).</param>
/// <param name="Due">When it may run (UTC): when it was enqueued for, or the end of a retry's backoff.</param>
/// <param name="Started">When it was claimed to run (UTC), or null when it never has been.</param>
/// <param name="Attempts">How many times it has been tried, the running attempt included.</param>
/// <param name="Stopping">It was asked to stop and has not ended yet.</param>
public sealed record LaneJob(long Id, IJob? Job, string Type, DateTime Due, DateTime? Started, int Attempts, bool Stopping);
