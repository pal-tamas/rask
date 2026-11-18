using Rask.Batteries;
using Rask.Cqrs;
using Rask.Data;

namespace Rask.Background;

/// <summary>Every job a test enqueued, and the sentences that ask about them.</summary>
public sealed class JobsFake : IJobs, IDisposable
{
    private readonly List<EnqueuedJob> _enqueued = [];
    private readonly IJobs? _previous;
    private readonly Lock _gate = new();
    private long _ids;

    internal JobsFake()
    {
        _previous = Jobs.Faked.Value;
        Jobs.Faked.Value = this;
    }

    /// <summary>
    ///     Asks about what was enqueued: <c>jobs.Enqueued&lt;SendWelcome&gt;().Once()</c>,
    ///     <c>jobs.Enqueued&lt;ChaseInvoice&gt;().In(24.Hours).Once()</c>, <c>jobs.Enqueued&lt;Backup&gt;().None()</c>.
    ///     <c>Single()</c> hands back the one that matched, to assert on the job itself.
    /// </summary>
    /// <typeparam name="TJob">The job to ask about.</typeparam>
    public Counting<EnqueuedJob<TJob>> Enqueued<TJob>()
        where TJob : IJob
    {
        List<EnqueuedJob<TJob>> matching;
        string others;
        lock (_gate)
        {
            matching = [.. _enqueued
                .Where(e => e.Job is TJob)
                .Select(e => new EnqueuedJob<TJob>((TJob)e.Job, e.Delay, e.Moment, e.Lane))];

            // What was enqueued INSTEAD — the half of a failing expectation that says where to look. It
            // cannot travel as EnqueuedJob<TJob>, so it is built here, while the list is still untyped.
            others = _enqueued.Count == 0
                ? "Nothing was enqueued at all."
                : $"Enqueued instead: {string.Join(", ", _enqueued.Select(e => e.Job.GetType().Name))}.";
        }

        return new Counting<EnqueuedJob<TJob>>(matching, typeof(TJob).Name, "enqueued", Describe, others);

        // Described by kind and timing, because a job is a record whose ToString is its whole payload —
        // too long to read in a failure message, and the kind is what a test is usually asking about.
        static string Describe(EnqueuedJob<TJob> e) => e switch
        {
            { Delay: { } d } => $"{typeof(TJob).Name} in {d}",
            { Moment: { } m } => $"{typeof(TJob).Name} at {m:u}",
            _ => typeof(TJob).Name,
        };
    }

    /// <summary>
    ///     Runs every recorded job that has not run yet through its real handler, as the tenant and user
    ///     who enqueued it: <c>await jobs.Run();</c>. A job enqueued by a running handler is recorded and
    ///     runs in the same call, and a job held back with <c>.In(…)</c> or <c>.At(…)</c> runs too — the
    ///     fake has no clock to wait on. A handler that throws lets its exception out.
    /// </summary>
    /// <remarks>
    ///     A job enqueued <c>.Noticed(…)</c> by a signed-in user publishes its <see cref="WorkReported" />s as the processor
    ///     would — running, its reports (<c>Jobs.Report(…)</c>, the latest of each quarter second), then done or failed — so a
    ///     test can subscribe to its own work and see it move.
    /// </remarks>
    public async Task Run(CancellationToken cancellationToken = default)
    {
        while (Next() is { } next)
        {
            using var tenant = next.Tenant is { } owner ? Tenant.Use(owner) : null;
            using var user = Current.UseUser(next.User);
            await Noticed(next, () => Dispatcher.Send(next.Job, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>How many jobs of every kind were enqueued.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _enqueued.Count;
            }
        }
    }

    /// <summary>Forgets everything recorded, without putting the real queue back.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _enqueued.Clear();
        }
    }

    /// <summary>Puts the real job queue back.</summary>
    public void Dispose() => Jobs.Faked.Value = _previous;

    Task IJobs.Add(IJob job, DateTimeOffset? at, TimeSpan? after, string? lane, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_gate)
        {
            _enqueued.Add(new EnqueuedJob(job, after, at, Tenant.Key, CurrentUser.Id, lane, JobNotice.Pending));
        }

        return Task.CompletedTask;
    }

    // Waiting here is "recorded and not yet run": the fake has no claim, and Run() is what takes a job off the list.
    Task<bool> IJobs.AddUnlessWaiting(IJob job, DateTimeOffset? at, TimeSpan? after, string? lane, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var tenant = Tenant.Key;
        lock (_gate)
        {
            foreach (var waiting in _enqueued.Where(e => !e.Ran))
            {
                if (string.Equals(waiting.Lane, lane, StringComparison.Ordinal) && waiting.Tenant == tenant && Equals(waiting.Job, job))
                {
                    return Task.FromResult(false);
                }
            }

            _enqueued.Add(new EnqueuedJob(job, after, at, tenant, CurrentUser.Id, lane, JobNotice.Pending));
        }

        return Task.FromResult(true);
    }

    // An awaited job cannot wait for the test to say Run(): it is recorded, already run, and runs here through its real
    // handler - so the caller gets the handler's own answer, or its exception, as it would from the processor.
    async Task<TResult> IJobs.Run<TResult>(IJob<TResult> job, string? lane, TimeSpan? within, CancellationToken cancellationToken)
    {
        var run = RecordRun(job, lane);
        TResult answer = default!;
        await Noticed(run, async () => answer = await Dispatcher.Send<TResult>(job, cancellationToken).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);
        return answer;
    }

    Task IJobs.Run(IJob job, string? lane, TimeSpan? within, CancellationToken cancellationToken) =>
        Noticed(RecordRun(job, lane), () => Dispatcher.Send(job, cancellationToken), cancellationToken);

    // Nothing runs on its own here, so nothing can be stopped.
    Task<bool> IJobs.Stop(long id, CancellationToken cancellationToken) => Task.FromResult(false);

    // The fake holds no rows, so every lane asked for is idle.
    Task<IReadOnlyDictionary<string, JobLane>> IJobs.Lanes(IEnumerable<string> lanes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lanes);
        IReadOnlyDictionary<string, JobLane> idle = lanes
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(lane => lane, lane => new JobLane(lane, null, []), StringComparer.Ordinal);
        return Task.FromResult(idle);
    }

    private EnqueuedJob RecordRun(IJob job, string? lane)
    {
        ArgumentNullException.ThrowIfNull(job);
        var run = new EnqueuedJob(job, null, null, Tenant.Key, CurrentUser.Id, lane, JobNotice.Pending) { Ran = true };
        lock (_gate)
        {
            _enqueued.Add(run);
        }

        return run;
    }

    // A job its enqueuer watches runs as a run of its own, so Jobs.Report reaches it; what it says is published in order once
    // its handler returns — running first, its reports, then how it ended. A job nobody watches just runs.
    private async Task Noticed(EnqueuedJob job, Func<Task> handle, CancellationToken cancellationToken)
    {
        if (job.Notice is null || job.User is null)
        {
            await handle().ConfigureAwait(false);
            return;
        }

        var said = new List<WorkReported>();
        var run = new JobRun(
            Interlocked.Increment(ref _ids), JobSerializerRegistry.TypeName(job.Job.GetType()), job.Lane, job.Notice, job.User, job.Tenant,
            TimeProvider.System, cancellationToken);
        run.Say = said.Add;
        said.Add(run.ToWork(WorkState.Running, default));
        var ended = WorkState.Done;
        try
        {
            using var inFlight = JobInFlight.Enter(run);
            await handle().ConfigureAwait(false);
        }
        catch
        {
            ended = WorkState.Failed;
            throw;
        }
        finally
        {
            run.SayUnsaid();
            run.End();
            said.Add(run.ToWork(ended, run.Latest));
            run.Release();
            foreach (var work in said)
            {
                await Dispatcher.Publish(work, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private EnqueuedJob? Next()
    {
        lock (_gate)
        {
            var next = _enqueued.Find(e => !e.Ran);
            next?.Ran = true;
            return next;
        }
    }

    private sealed record EnqueuedJob(IJob Job, TimeSpan? Delay, DateTimeOffset? Moment, Guid? Tenant, Guid? User, string? Lane, string? Notice)
    {
        public bool Ran { get; set; }
    }
}
