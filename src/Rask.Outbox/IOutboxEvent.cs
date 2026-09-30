using Rask.Cqrs;

namespace Rask.Outbox;

/// <summary>
/// A domain event routed through the transactional outbox: it is written to the <see cref="OutboxMessage"/>
/// table in the <b>same transaction</b> as the entity change that raised it, then published by the
/// background <see cref="OutboxProcessor{TContext}"/> after it commits. It is a <see cref="IEvent"/>,
/// so the same <see cref="IEventHandler{TEvent}"/> handles it whether it is delivered
/// in-process (Rask.Data) or via the outbox.
/// </summary>
[LocalOnly]
public interface IOutboxEvent : IEvent;
