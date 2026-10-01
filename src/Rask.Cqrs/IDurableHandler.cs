namespace Rask.Cqrs;

/// <summary>
/// Handles an <see cref="IEvent"/> <b>durably</b>: the event is written to the transactional outbox in the same
/// transaction as the change that raised it, and this handler runs from there after the commit — retried until it
/// succeeds, and run again after a crash rather than lost. Each durable handler is its own outbox row, so one that
/// fails is retried alone.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="IEventHandler{TEvent}"/> of the same event runs in memory straight after the commit instead; the
/// two sit side by side, and the handler, not the event, chooses:
/// </para>
/// <code>
/// public sealed class RefreshDashboard : IEventHandler&lt;OrderPlaced&gt; { … }     // at once, in memory
/// public sealed class SendReceipt : IDurableHandler&lt;OrderPlaced&gt; { … }        // via the outbox, retried
/// </code>
/// <para>
/// Delivery is at-least-once, so make <see cref="Handle"/> safe to run twice. With no outbox in the app (a browser
/// app, or Rask.Cqrs on its own) a durable handler runs in memory like any other.
/// </para>
/// </remarks>
/// <typeparam name="TEvent">The event type.</typeparam>
public interface IDurableHandler<in TEvent>
    where TEvent : IEvent
{
    /// <summary>Reacts to the event. Cancelled with the outbox's shutdown grace.</summary>
    Task Handle(TEvent e);
}
