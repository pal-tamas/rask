using Rask.Data;

namespace Rask.Outbox;

/// <summary>
/// Claims domain-event delivery for the transactional outbox, so <c>Rask.Data</c>'s in-process publisher
/// stands down rather than draining events the <see cref="OutboxInterceptor"/> has not copied yet.
/// Registered by <see cref="RaskOutboxServiceCollectionExtensions.AddRaskOutbox{TContext}"/>; it carries no
/// behaviour, because its presence in the container is the entire signal.
/// </summary>
internal sealed class OutboxDeliveryOwner : IDomainEventDeliveryOwner
{
    /// <inheritdoc/>
    public string Name => "Rask.Outbox";
}
