using System.ComponentModel;

namespace Rask.Cqrs;

/// <summary>
///     Where an aggregate's <c>static Task On(E e)</c> runs when the app declared somewhere else for it than the outbox's
///     own drain — a job, for Rask.Jobs' <c>b.Job&lt;E&gt;()</c>.
/// </summary>
/// <remarks>Public only so the jobs package can plug in; an application does not implement it.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IAggregateEventRoute
{
    /// <summary>Whether <paramref name="aggregate" />'s handler of <paramref name="eventType" /> runs by this route.</summary>
    /// <param name="aggregate">The aggregate that declares the handler.</param>
    /// <param name="eventType">The event it handles.</param>
    bool Takes(Type aggregate, Type eventType);

    /// <summary>
    ///     Takes the event, to run its handler later through <see cref="AggregateEvents.Run" /> with
    ///     <paramref name="handler" />.
    /// </summary>
    /// <param name="aggregate">The aggregate that declares the handler.</param>
    /// <param name="e">The event.</param>
    /// <param name="handler">The handler's stored name.</param>
    /// <param name="cancellationToken">Cancels the hand-off.</param>
    Task HandOff(Type aggregate, IEvent e, string handler, CancellationToken cancellationToken);
}
