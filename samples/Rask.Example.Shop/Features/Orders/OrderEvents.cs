namespace Rask.Example.Shop.Features.Orders;

public sealed record OrderCreated(Guid Id) : IOutboxEvent;

public sealed record OrderUpdated(Guid Id) : IOutboxEvent;

public sealed record OrderDeleted(Guid Id) : IOutboxEvent;
