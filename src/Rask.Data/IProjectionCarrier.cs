using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
///     A row a declared projection's event rides on: raised onto it in the save that changed it, so the outbox stores it
///     in that save and the in-memory handlers hear it after the commit — exactly as an event the aggregate raised itself.
/// </summary>
internal interface IProjectionCarrier
{
    /// <summary>Adds <paramref name="projected" /> to the row's domain events.</summary>
    void Raise(IEvent projected);

    /// <summary>Takes back every event of the row's that <paramref name="projected" /> says a projection raised.</summary>
    void Withdraw(Predicate<IEvent> projected);
}
