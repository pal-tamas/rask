namespace Rask.Cqrs;

/// <summary>Controls how <see cref="IDispatcher.Publish{TEvent}"/> runs an event's handlers.</summary>
public enum EventPublishStrategy
{
    /// <summary>
    /// Run handlers one after another in registration order (the default). With
    /// <see cref="CqrsOptions.StopOnFirstEventException"/> set, the first failure stops the
    /// run and is rethrown; otherwise every handler runs and failures are aggregated.
    /// </summary>
    Sequential,

    /// <summary>
    /// Start every handler and await them together with <see cref="Task.WhenAll(Task[])"/>. All
    /// handlers run; the first failure surfaces (the rest are on the returned task's exceptions).
    /// </summary>
    WhenAll,
}
