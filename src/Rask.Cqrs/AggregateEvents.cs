using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs;

/// <summary>
///     Runs an aggregate's <c>static Task On(E e)</c> — the durable handler the generator makes of it — or hands it to the
///     route the app declared for it (<c>b.Job&lt;E&gt;()</c> in the aggregate's <c>Configure</c>, from Rask.Jobs).
/// </summary>
/// <remarks>Public only so generated code can call it; an application does not.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AggregateEvents
{
    // Set while a route runs the handler it took, so the run is the handler's and not a second hand-off.
    private static readonly AsyncLocal<bool> Routed = new();

    /// <summary>Runs <paramref name="on" /> for <paramref name="e" />, or hands it to its declared route.</summary>
    /// <typeparam name="TAggregate">The aggregate that declares the handler.</typeparam>
    /// <typeparam name="TEvent">The event it handles.</typeparam>
    /// <param name="provider">The scope the handler runs in.</param>
    /// <param name="e">The event.</param>
    /// <param name="on">The aggregate's <c>On</c>.</param>
    /// <param name="cancellationToken">Cancels a hand-off.</param>
    /// <returns>A task that completes once the handler ran, or once the route took it.</returns>
    public static Task Handle<TAggregate, TEvent>(
        IServiceProvider provider, TEvent e, Func<TEvent, Task> on, CancellationToken cancellationToken)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(on);
        if (!Routed.Value && provider.GetService<IAggregateEventRoute>() is { } route &&
            route.Takes(typeof(TAggregate), typeof(TEvent)))
        {
            return route.HandOff(typeof(TAggregate), e, CqrsRegistry.DurableNameOf(typeof(TAggregate), typeof(TEvent)), cancellationToken);
        }

        return on(e);
    }

    /// <summary>
    ///     Runs the durable handler stored as <paramref name="handler" /> for <paramref name="e" /> itself — what a route
    ///     calls once it is the route's turn.
    /// </summary>
    /// <param name="provider">The scope the handler runs in.</param>
    /// <param name="handler">The handler's stored name, as <see cref="IAggregateEventRoute.HandOff" /> was given it.</param>
    /// <param name="e">The event.</param>
    /// <param name="cancellationToken">Cancels the handler.</param>
    /// <returns>A task that completes once the handler ran.</returns>
    /// <exception cref="InvalidOperationException">No handler of that name is loaded any more.</exception>
    public static async Task Run(IServiceProvider provider, string handler, IEvent e, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var invoker = CqrsRegistry.FindDurableHandler(handler)
            ?? throw new InvalidOperationException(
                $"No durable handler '{handler}'. It was renamed or deleted after this event was handed to it.");
        var previous = Routed.Value;
        Routed.Value = true;
        try
        {
            await invoker(provider, e, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Routed.Value = previous;
        }
    }
}
