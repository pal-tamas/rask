namespace Rask;

/// <summary>
///     Where an event a dispatcher published meets the components of this process's live pages that handle it — a
///     component that implements <c>IEventHandler&lt;T&gt;</c>.
/// </summary>
/// <remarks>
///     Here because both sides reach it and neither reaches the other: Rask.Cqrs publishes and installs the generated
///     table of which component handles which event, Rask.Core tracks the mounted ones and runs each in its own
///     session, and Rask.Data holds a delivery back until its transaction commits.
/// </remarks>
internal static class LiveEvents
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<object, (Type Component, Type Event, Func<object, object, Task> Handle)[]> Groups = [];
    private static volatile Dictionary<Type, Listener[]> _byComponent = [];

    /// <summary>One event a component type handles, and how to hand it one.</summary>
    internal readonly record struct Listener(Type Event, Func<object, object, Task> Handle);

    /// <summary>Whether any component of the app handles an event: false, and mounting one asks nothing more.</summary>
    internal static bool Any => _byComponent.Count > 0;

    /// <summary>Hands an event to every mounted component that handles it, each in its own session. Set by Rask.Core.</summary>
    internal static Action<object>? Deliver { get; set; }

    /// <summary>
    ///     Holds <c>work</c> until the transaction in progress commits, and drops it on a rollback; false when no
    ///     transaction is in progress. Set by Rask.Data.
    /// </summary>
    internal static Func<Action, bool>? DeferUntilCommit { get; set; }

    /// <summary>What <paramref name="component" /> handles, or null when it handles nothing.</summary>
    internal static Listener[]? Of(Type component) =>
        _byComponent.TryGetValue(component, out var listeners) ? listeners : null;

    /// <summary>Hands <paramref name="e" /> to the components that handle it — after the commit, inside a transaction.</summary>
    internal static void Publish(object e)
    {
        if (Deliver is not { } deliver)
        {
            return;
        }

        if (DeferUntilCommit?.Invoke(() => deliver(e)) == true)
        {
            return;
        }

        deliver(e);
    }

    /// <summary>Installs one assembly's whole table (generated), replacing what it installed before.</summary>
    internal static void Replace(object group, (Type Component, Type Event, Func<object, object, Task> Handle)[] listeners)
    {
        lock (Gate)
        {
            Groups[group] = listeners;
            _byComponent = Groups.Values
                .SelectMany(static l => l)
                .GroupBy(static l => l.Component)
                .ToDictionary(static g => g.Key, static g => g.Select(static l => new Listener(l.Event, l.Handle)).ToArray());
        }
    }
}
