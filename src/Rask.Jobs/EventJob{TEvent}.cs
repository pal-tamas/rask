using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
///     How an aggregate's <c>static Task On(TEvent e)</c> runs as a job — declared in its <c>Configure</c> with
///     <c>b.Job&lt;TEvent&gt;()</c>, then shaped by these steps.
/// </summary>
/// <remarks>
///     <code>
/// public static void Configure(EntityTypeBuilder&lt;QueuedFile&gt; b) =>
///     b.Job&lt;FileQueued&gt;().InTenantLane("loads").UnlessWaiting(e => e.File.ToString());
///     </code>
///     The steps are the ones <c>Jobs.Enqueue(job)</c> takes, read off the event: a later step of the same kind replaces
///     an earlier one, and <see cref="InTenantLane" /> and <see cref="InLane" /> replace each other.
/// </remarks>
/// <typeparam name="TEvent">The event the job runs the handler of.</typeparam>
public sealed class EventJob<TEvent>
    where TEvent : class, IEvent
{
    private readonly EventJobRoute _route;

    internal EventJob(EventJobRoute route) => _route = route;

    /// <summary>
    ///     Runs it in the lane of the tenant the event was raised for: one at a time with that tenant's other work of
    ///     <paramref name="lane" />, while every other tenant's runs beside it — <c>.InTenantLane("loads")</c>.
    /// </summary>
    /// <param name="lane">Which of the tenant's lanes, or null for its one lane.</param>
    /// <returns>These steps, for the next one.</returns>
    public EventJob<TEvent> InTenantLane(string? lane = null)
    {
        if (lane is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(lane);
        }

        _route.Lane = _ => Jobs.TenantLane(lane);
        return this;
    }

    /// <summary>
    ///     Runs it in a lane the event names: one at a time with the lane's other jobs — <c>.InLane(e => e.Pump.ToString())</c>.
    /// </summary>
    /// <param name="lane">The lane, read off the event.</param>
    /// <returns>These steps, for the next one.</returns>
    public EventJob<TEvent> InLane(Func<TEvent, string> lane)
    {
        ArgumentNullException.ThrowIfNull(lane);
        _route.Lane = e => lane((TEvent)e);
        return this;
    }

    /// <summary>
    ///     Enqueues nothing when work of the same key already waits in its lane, unclaimed —
    ///     <c>.UnlessWaiting(e => e.Company.ToString())</c>: two events for one company are one piece of work.
    /// </summary>
    /// <param name="key">What names the work, read off the event — at most 1,024 characters.</param>
    /// <returns>These steps, for the next one.</returns>
    public EventJob<TEvent> UnlessWaiting(Func<TEvent, string> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _route.Key = e => key((TEvent)e);
        return this;
    }
}
