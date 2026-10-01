using System.ComponentModel;

namespace Rask.Cqrs;

/// <summary>
/// Where an event's <see cref="IDurableHandler{TEvent}"/>s are written, so they run later from there. The transactional
/// outbox registers one; with none registered, durable handlers run in memory like any other handler.
/// </summary>
/// <remarks>Public only so the outbox package can plug in; an application does not implement it.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IDurableEventStore
{
    /// <summary>Stores <paramref name="e"/> once per handler in <paramref name="handlers"/>, in its own transaction.</summary>
    /// <param name="e">The event.</param>
    /// <param name="handlers">The durable handlers to store it for, from <see cref="CqrsRegistry.DurableHandlersOf"/>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task Store(IEvent e, IReadOnlyList<string> handlers, CancellationToken cancellationToken);
}
