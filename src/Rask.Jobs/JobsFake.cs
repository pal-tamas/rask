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
    private int _ran;

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
                .Select(e => new EnqueuedJob<TJob>((TJob)e.Job, e.Delay, e.Moment))];

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
    public async Task Run(CancellationToken cancellationToken = default)
    {
        while (Next() is { } next)
        {
            using var tenant = next.Tenant is { } owner ? Tenant.Use(owner) : null;
            using var user = Current.UseUser(next.User);
            await Dispatcher.Send(next.Job, cancellationToken).ConfigureAwait(false);
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
            _ran = 0;
        }
    }

    /// <summary>Puts the real job queue back.</summary>
    public void Dispose() => Jobs.Faked.Value = _previous;

    Task IJobs.Add(IJob job, DateTimeOffset? at, TimeSpan? after, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_gate)
        {
            _enqueued.Add(new EnqueuedJob(job, after, at, Current.Tenant, Current.UserId));
        }

        return Task.CompletedTask;
    }

    private EnqueuedJob? Next()
    {
        lock (_gate)
        {
            return _ran < _enqueued.Count ? _enqueued[_ran++] : null;
        }
    }

    private sealed record EnqueuedJob(IJob Job, TimeSpan? Delay, DateTimeOffset? Moment, Guid? Tenant, Guid? User);
}
