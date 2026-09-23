using Rask.Batteries;

namespace Rask.Jobs;

/// <summary>A test's stand-in for the job queue: <c>using var jobs = Jobs.Fake();</c>.</summary>
public static class JobsFakes
{
    extension(Jobs)
    {
        /// <summary>
        ///     Takes the place of the job queue for this test, recording every enqueue instead of writing a
        ///     row, until the returned fake is disposed:
        /// </summary>
        /// <remarks>
        ///     <code>
        ///     using var jobs = Jobs.Fake();
        ///
        ///     await page.Click("Place order");
        ///
        ///     jobs.Enqueued&lt;SendOrderReceipt&gt;().Once();
        ///     jobs.Enqueued&lt;ChaseInvoice&gt;().In(24.Hours).Once();
        ///     </code>
        ///     <para>
        ///         Nothing runs: a recorded job is never dispatched to its handler, which is the point —
        ///         the test asserts that the work was <em>asked for</em>, and the handler has its own test.
        ///         Scoped to the test's own flow, so tests running in parallel never see each other's jobs.
        ///         It stands in front of <c>Jobs.Enqueue</c>; a class that takes <see cref="IJobs" /> in its
        ///         constructor is handed whatever the container holds, so register the fake there too —
        ///         <c>services.AddSingleton&lt;IJobs&gt;(jobs)</c> — when the code under test injects it.
        ///     </para>
        /// </remarks>
        public static JobsFake Fake() => new();
    }
}

/// <summary>Every job a test enqueued, and the sentences that ask about them.</summary>
public sealed class JobsFake : IJobs, IDisposable
{
    private readonly List<EnqueuedJob> _enqueued = [];
    private readonly IJobs? _previous;
    private readonly Lock _gate = new();

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
        static string Describe(EnqueuedJob<TJob> e) =>
            e.Delay is { } d ? $"{typeof(TJob).Name} in {d}"
            : e.Moment is { } m ? $"{typeof(TJob).Name} at {m:u}"
            : typeof(TJob).Name;
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

    Task IJobs.Add(IJob job, DateTimeOffset? at, TimeSpan? after, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_gate)
        {
            _enqueued.Add(new EnqueuedJob(job, after, at));
        }

        return Task.CompletedTask;
    }

    private sealed record EnqueuedJob(IJob Job, TimeSpan? Delay, DateTimeOffset? Moment);
}

/// <summary>One job a test enqueued.</summary>
/// <typeparam name="TJob">The job's type.</typeparam>
/// <param name="Job">The job itself, to assert on its own properties.</param>
/// <param name="Delay">What <c>.In(…)</c> asked for, or <c>null</c>.</param>
/// <param name="Moment">What <c>.At(…)</c> asked for, or <c>null</c>.</param>
public sealed record EnqueuedJob<TJob>(TJob Job, TimeSpan? Delay, DateTimeOffset? Moment)
    where TJob : IJob;

/// <summary>The steps that narrow what a test asks about its jobs.</summary>
public static class EnqueuedJobCounting
{
    extension<TJob>(Counting<EnqueuedJob<TJob>> enqueued)
        where TJob : IJob
    {
        /// <summary>Only the jobs held back by <c>.In(<paramref name="delay" />)</c>.</summary>
        public Counting<EnqueuedJob<TJob>> In(TimeSpan delay) =>
            enqueued.Where(e => e.Delay == delay, $"in {delay}");

        /// <summary>Only the jobs held back until <c>.At(<paramref name="moment" />)</c>.</summary>
        public Counting<EnqueuedJob<TJob>> At(DateTimeOffset moment) =>
            enqueued.Where(e => e.Moment == moment, $"at {moment:u}");

        /// <summary>Only the jobs the <paramref name="matches" /> predicate accepts.</summary>
        /// <param name="matches">What to keep.</param>
        /// <param name="said">How the step reads in a failure message, e.g. <c>for order 7</c>.</param>
        public Counting<EnqueuedJob<TJob>> Matching(Func<TJob, bool> matches, string said)
        {
            ArgumentNullException.ThrowIfNull(matches);
            return enqueued.Where(e => matches(e.Job), said);
        }
    }
}
