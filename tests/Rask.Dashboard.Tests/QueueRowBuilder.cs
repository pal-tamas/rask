using System.Runtime.CompilerServices;
using Rask.Background;

namespace Rask.Dashboard.Tests;

/// <summary>
///     Builds a queue row in a state the production API cannot reach on its own.
/// </summary>
/// <remarks>
///     The dashboard's panels exist to show rows mid-flight: a dead letter that burned its attempts, a row
///     already processed. <c>Attempts</c> is advanced by the processor's <c>ExecuteUpdate</c>, so there is no
///     setter for it and there should not be. Written here through the entity's own private state with
///     <c>[UnsafeAccessor]</c> — the mechanism Rask's generated writes already use — rather than widening
///     <see cref="Job" /> so a test can reach it.
/// </remarks>
internal static class QueueRowBuilder
{
    /// <summary>A job in whatever state the panel under test needs to render.</summary>
    /// <param name="runAt">When it was due.</param>
    /// <param name="attempts">How many attempts it has burned.</param>
    /// <param name="processedAt">When it completed, or <c>null</c> while it is outstanding.</param>
    /// <param name="error">Its last failure, if any.</param>
    internal static Job Job(
        DateTime runAt, int attempts = 0, DateTime? processedAt = null, string? error = null)
    {
        var job = Rask.Background.Job.For("Some.Job", "{}", runAt);

        if (error is not null)
        {
            job.Failed(error, runAt);
        }

        if (processedAt is { } completed)
        {
            job.Completed(completed);
        }

        Attempts(job) = attempts;
        return job;
    }

    /// <summary>A job that is out of attempts and unprocessed — a dead letter.</summary>
    /// <param name="runAt">When it was due.</param>
    /// <param name="attempts">How many attempts it burned.</param>
    /// <param name="error">Its last failure.</param>
    internal static Job DeadLetter(DateTime runAt, int attempts, string error = "boom") =>
        Job(runAt, attempts, processedAt: null, error);

    /// <summary>A job a processor holds right now: claimed until <paramref name="claimedUntil"/>, in <paramref name="lane"/>.</summary>
    /// <param name="startedAt">When it was claimed.</param>
    /// <param name="claimedUntil">When its claim runs out.</param>
    /// <param name="lane">Its lane.</param>
    /// <param name="attempts">How many attempts it has started, this one included.</param>
    /// <param name="stopped">The stop asked of it, if one was.</param>
    internal static Job Running(DateTime startedAt, DateTime claimedUntil, string? lane = null, int attempts = 1, JobStop? stopped = null)
    {
        var job = Rask.Background.Job.For("Some.Job", "{}", startedAt, lane);
        Attempts(job) = attempts;
        ClaimToken(job) = Guid.NewGuid();
        ClaimedUntil(job) = claimedUntil;
        StartedAt(job) = startedAt;
        Stopped(job) = stopped;
        return job;
    }

    /// <summary>A job that ended stopped, for <paramref name="why"/>.</summary>
    /// <param name="at">When it ended.</param>
    /// <param name="why">Why it was stopped.</param>
    internal static Job EndedStopped(DateTime at, JobStop why)
    {
        var job = Job(at, attempts: 1, processedAt: at);
        Stopped(job) = why;
        return job;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<Attempts>k__BackingField")]
    private static extern ref int Attempts(Job job);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<ClaimToken>k__BackingField")]
    private static extern ref Guid? ClaimToken(Job job);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<ClaimedUntil>k__BackingField")]
    private static extern ref DateTime? ClaimedUntil(Job job);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<StartedAt>k__BackingField")]
    private static extern ref DateTime? StartedAt(Job job);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<Stopped>k__BackingField")]
    private static extern ref JobStop? Stopped(Job job);
}
