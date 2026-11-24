using System.ComponentModel;

namespace Rask.Cqrs;

/// <summary>
///     The stored event a durable handler is running for — <see cref="Current" /> inside an
///     <see cref="IDurableHandler{TEvent}" /> or an aggregate's <c>static Task On(E e)</c> run by the outbox.
/// </summary>
/// <remarks>
///     <para>
///         Delivery is at-least-once, so a handler that calls something outside the database (a payment, a partner's API)
///         passes <see cref="MessageId" /> as that call's idempotency key: a second run of the same row sends the same key,
///         and the other side does it once.
///     </para>
///     <code>
/// await partner.Charge(e.Order, idempotencyKey: $"charge-{DurableEvent.Current?.MessageId}");
///     </code>
/// </remarks>
public sealed class DurableEvent
{
    private static readonly AsyncLocal<DurableEvent?> InFlight = new();

    /// <summary>Names the stored event a handler is about to run for. Made by the outbox.</summary>
    /// <param name="messageId">The outbox row's id.</param>
    /// <param name="attempt">Which attempt this is, from 1.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public DurableEvent(long messageId, int attempt)
    {
        MessageId = messageId;
        Attempt = attempt;
    }

    /// <summary>The outbox row's id: the same on every attempt of this event for this handler.</summary>
    public long MessageId { get; }

    /// <summary>Which attempt this is, from 1.</summary>
    public int Attempt { get; }

    /// <summary>
    ///     The stored event the running handler is handling, or <see langword="null" /> outside one — in an
    ///     <see cref="IEventHandler{TEvent}" />, or a durable handler run in memory because the app has no outbox.
    /// </summary>
    public static DurableEvent? Current => InFlight.Value;

    /// <summary>Makes <paramref name="running" /> <see cref="Current" /> until disposed. Called by the outbox.</summary>
    /// <param name="running">The stored event about to be handled.</param>
    /// <returns>Disposing it puts back what was current before.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IDisposable Use(DurableEvent running)
    {
        ArgumentNullException.ThrowIfNull(running);
        var previous = InFlight.Value;
        InFlight.Value = running;
        return new Restore(previous);
    }

    private sealed class Restore(DurableEvent? previous) : IDisposable
    {
        public void Dispose() => InFlight.Value = previous;
    }
}
