using System.Collections.Concurrent;
using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
///     What <c>b.Job&lt;E&gt;()</c> declared for one aggregate's handler of one event: the lane its job runs in and the key
///     that makes a second one wait for nothing. Kept per process — a model is built once, and every model of the app
///     declares the same.
/// </summary>
internal sealed class EventJobRoute
{
    private static readonly ConcurrentDictionary<(Type Aggregate, Type Event), EventJobRoute> Declared = new();

    /// <summary>The lane, read off the event; null for none.</summary>
    public Func<IEvent, string>? Lane { get; set; }

    /// <summary>The key of <c>UnlessWaiting</c>, read off the event; null to enqueue every one.</summary>
    public Func<IEvent, string>? Key { get; set; }

    /// <summary>The route for <paramref name="aggregate" />'s handler of <paramref name="eventType" />, made fresh.</summary>
    public static EventJobRoute Declare(Type aggregate, Type eventType)
    {
        // Fresh on every declaration: a model built again (another context type, a test) states the whole route again.
        var route = new EventJobRoute();
        Declared[(aggregate, eventType)] = route;
        return route;
    }

    /// <summary>The route declared for the pair, or null when it runs where the outbox runs it.</summary>
    public static EventJobRoute? Of(Type aggregate, Type eventType) =>
        Declared.TryGetValue((aggregate, eventType), out var route) ? route : null;
}
