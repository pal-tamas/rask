using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
///     Hands an aggregate's <c>static Task On(E e)</c> to a job when its <c>Configure</c> declared <c>b.Job&lt;E&gt;()</c>:
///     the outbox's row then only enqueues it — in the lane and under the key the declaration reads off the event — and
///     the job runs the handler, with the job's lease, progress and dead letter.
/// </summary>
internal sealed class AggregateEventJobRoute : IAggregateEventRoute
{
    public bool Takes(Type aggregate, Type eventType) => EventJobRoute.Of(aggregate, eventType) is not null;

    public async Task HandOff(Type aggregate, IEvent e, string handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(e);
        var route = EventJobRoute.Of(aggregate, e.GetType())
            ?? throw new InvalidOperationException($"'{aggregate.Name}' declares no job for '{e.GetType().Name}'.");

        var enqueuing = Jobs.Enqueue(AggregateEventJob.For(handler, e), cancellationToken);
        if (route.Lane is { } lane)
        {
            enqueuing = enqueuing.InLane(lane(e));
        }

        if (route.Key is { } key)
        {
            // Every aggregate's handler is the one job kind, so the key is the handler's: two handlers never share one.
            enqueuing = enqueuing.UnlessWaiting($"{handler}|{key(e)}");
        }

        await enqueuing.AsTask().ConfigureAwait(false);
    }
}
