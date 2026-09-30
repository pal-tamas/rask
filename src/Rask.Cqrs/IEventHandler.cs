namespace Rask.Cqrs;

/// <summary>
/// Handles an <see cref="IEvent"/>. Any number of handlers may exist for one event;
/// all of them run when it is published.
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
#pragma warning disable CA1711 // an interface, not a delegate: the name pairs with ICommandHandler/IQueryHandler (owner's call)
public interface IEventHandler<in TEvent>
#pragma warning restore CA1711
    where TEvent : IEvent
{
    /// <summary>Reacts to the published event. Cancelled with the work that published it.</summary>
    Task Handle(TEvent e);
}
