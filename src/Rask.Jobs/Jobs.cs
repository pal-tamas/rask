using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Background;

/// <summary>
///     The app's job queue, with nothing injected — from a handler, a render, a request, another job:
/// </summary>
/// <remarks>
///     <code>
///     await Jobs.Enqueue(new SendWelcome(user.Id));
///     await Jobs.Enqueue(new ChaseInvoice(id)).In(24.Hours);
///     await Jobs.Enqueue(new CloseBooks()).At(monthEnd);
///     </code>
///     <para>
///         Each call reaches the <see cref="IJobs" /> of the work it runs in and is cancelled with that work.
///         Outside any — a hosted service, a timer started at boot — it throws; inject <see cref="IJobs" />
///         there instead.
///     </para>
/// </remarks>
public static class Jobs
{
    /// <summary>Runs <paramref name="job" /> in the background, as soon as the processor next polls.</summary>
    public static Enqueuing Enqueue(IJob job, CancellationToken cancellationToken = default) =>
        new(null, job, null, null, null, cancellationToken);

    /// <summary>
    ///     Runs <paramref name="job" /> as a job and waits for its answer:
    ///     <c>var done = await Jobs.Run(new AtPump(pump.Id)).InLane(pump.Id);</c>. Stored and run like any job — in its
    ///     lane, one at a time with that lane's other jobs on every host — and tried once: a failure throws
    ///     <see cref="JobFailedException" /> here. Cancelling the wait, or <c>.Within(…)</c> running out, stops the waiting,
    ///     never the job.
    /// </summary>
    public static Running<TResult> Run<TResult>(IJob<TResult> job, CancellationToken cancellationToken = default) =>
        new(null, job, null, null, cancellationToken);

    /// <summary>
    ///     Runs <paramref name="job" /> as a job and waits until it has run: <c>await Jobs.Run(new Calibrate(pump.Id)).InLane(pump.Id);</c>.
    ///     As <see cref="Run{TResult}" />, for a job whose handler answers nothing.
    /// </summary>
    public static Running Run(IJob job, CancellationToken cancellationToken = default) =>
        new(null, job, null, null, cancellationToken);

    /// <summary>
    ///     Stops the job with this row id — the <see cref="LaneJob.Id" /> a lane's view shows, the row the dashboard lists:
    ///     <c>await Jobs.Stop(lane.Running!.Id)</c>. A running job's <c>Current.Cancellation</c> is cancelled, on whichever host
    ///     runs it, and it ends stopped (<see cref="JobStop.Asked" />); a waiting job never runs. Stopped is not failed: it is not
    ///     tried again. By id, never "whatever runs in the lane now", so a click that lands as one job ends never stops the next.
    ///     It checks no rights and sees every tenant's jobs: who may stop a job is the app's to decide, at the button.
    /// </summary>
    /// <returns><see langword="false" /> when the job is done already, stopped already, or does not exist.</returns>
    public static Task<bool> Stop(long id, CancellationToken cancellationToken = default) =>
        Resolve().Stop(id, Ambient.Or(cancellationToken));

    /// <summary>
    ///     Inside a job's handler: why this job was stopped, or null while it runs on. Null outside a job. For a handler that
    ///     says why on its own board: <c>catch (Exception) when (Jobs.Stopped is { } why) { … throw; }</c>.
    /// </summary>
    public static JobStop? Stopped => JobInFlight.Run?.Stopped;

    /// <summary>
    ///     Inside a job's handler: says how it gets on — <c>Jobs.Report($"{kept} of {total} rows")</c>. Every report is progress,
    ///     so it restarts the clock of <c>c.Jobs.Limit&lt;TJob&gt;().IdleFor(…)</c>. Kept in memory, on the host running the job.
    ///     Outside a job it does nothing.
    /// </summary>
    /// <param name="progress">What the job has done so far, in words for a person.</param>
    public static void Report(string progress) => JobInFlight.Run?.Report(progress);

    /// <summary>
    ///     The state of these lanes in one read: <c>var lanes = await Jobs.Lanes(pumpIds);</c>. Every lane asked for is in the
    ///     answer — <see cref="JobLane.IsIdle" /> when nothing runs or waits in it.
    /// </summary>
    public static Task<IReadOnlyDictionary<string, JobLane>> Lanes(IEnumerable<string> lanes, CancellationToken cancellationToken = default) =>
        Resolve().Lanes(lanes, Ambient.Or(cancellationToken));

    /// <summary>The state of one lane: what runs in it, and what waits — <c>(await Jobs.Lane(pump.Id)).Running?.Job</c>.</summary>
    public static async Task<JobLane> Lane(string lane, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lane);
        var lanes = await Lanes([lane], cancellationToken).ConfigureAwait(false);
        return lanes[lane];
    }

    /// <summary>
    ///     Whether Rask.Jobs is registered at all. For an operator surface that renders "off" rather than
    ///     failing — <c>Rask.Dashboard</c> does exactly that. An app should not branch on this: a call with
    ///     nothing registered throws and names the registration that fixes it.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool IsOn =>
        Faked.Value is not null || Ambient.Services?.GetService<IJobs>() is not null;

    /// <summary>What <c>Jobs.Fake()</c> put in the way of the real queue, for this test's flow alone.</summary>
    internal static readonly AsyncLocal<IJobs?> Faked = new();

    internal static IJobs Resolve()
    {
        if (Faked.Value is { } fake)
        {
            return fake;
        }

        return Ambient.Reach<IJobs>("Jobs", "Program.cs says c.Jobs.Off()", "AddRaskJobs<AppDbContext>()");
    }
}
