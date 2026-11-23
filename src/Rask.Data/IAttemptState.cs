using Rask.Cqrs;

namespace Rask.Data;

/// <summary>What a save may change on an aggregate, kept so a rolled-back attempt can put it back (<see cref="AttemptUndo" />).</summary>
internal interface IAttemptState
{
    /// <summary>The aggregate as it stood before the save.</summary>
    AttemptState Before();

    /// <summary>Puts back <paramref name="before" />.</summary>
    void Restore(AttemptState before);

    /// <summary>Puts <paramref name="events" /> back in front of any raised since, as they were before the save took them.</summary>
    void PutBack(IReadOnlyList<IEvent> events);
}
