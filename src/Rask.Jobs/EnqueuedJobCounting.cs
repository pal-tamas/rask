using Rask.Batteries;

namespace Rask.Background;

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
