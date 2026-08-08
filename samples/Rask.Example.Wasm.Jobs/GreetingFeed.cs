namespace Rask.Example.Wasm.Jobs;

/// <summary>
///     How a job tells the UI that something changed. A singleton the handler raises and components
///     subscribe to — the same shape as any app-wide producer, and decoupled from the component tree so
///     the job never needs to know whether anything is watching.
/// </summary>
public sealed class GreetingFeed
{
    /// <summary>Raised after a job wrote a greeting. Subscribe on mount, unsubscribe on unmount.</summary>
    public event Action? Updated;

    public void Changed() => Updated?.Invoke();
}
