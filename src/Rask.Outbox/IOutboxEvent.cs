using Rask.Cqrs;

namespace Rask.Outbox;

/// <summary>
/// A domain event routed through the transactional outbox: it is written to the <see cref="OutboxMessage"/>
/// table in the <b>same transaction</b> as the aggregate change that raised it, then published by the
/// background <see cref="OutboxProcessor{TContext}"/> after it commits. It is a <see cref="INotification"/>,
/// so the same <see cref="INotificationHandler{TNotification}"/> handles it whether it is delivered
/// in-process (Rask.Data) or via the outbox.
/// </summary>
public interface IOutboxEvent : INotification;
