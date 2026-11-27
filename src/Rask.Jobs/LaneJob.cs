using Rask.Values;

namespace Rask.Background;

/// <summary>One job of a lane (<see cref="JobLane" />), as its row holds it.</summary>
/// <param name="Id">The job's row id: what <c>Jobs.Stop(id)</c> takes.</param>
/// <param name="Job">The job itself, or null when its payload no longer reads as a registered job — it is still listed, so it is seen.</param>
/// <param name="Type">The job's registered type name (<see cref="Background.Job.Type" />).</param>
/// <param name="Due">When it may run: when it was enqueued for, or the end of a retry's backoff.</param>
/// <param name="Started">When it was claimed to run, or null when it never has been.</param>
/// <param name="Attempts">How many times it has been tried, the running attempt included.</param>
/// <param name="Stopping">It was asked to stop and has not ended yet.</param>
/// <param name="Progress">What it last said of itself (<c>Jobs.Report(…)</c>), or null — the host running it says its latest.</param>
/// <param name="Done">How much of <paramref name="Of" /> it has done, when it reported a count (<c>Jobs.Report(done, of)</c>).</param>
/// <param name="Of">How much it has to do, when it reported a count.</param>
public sealed record LaneJob(
    Guid Id,
    IJob? Job,
    string Type,
    Timestamp Due,
    Timestamp? Started,
    int Attempts,
    bool Stopping,
    string? Progress = null,
    int? Done = null,
    int? Of = null);
