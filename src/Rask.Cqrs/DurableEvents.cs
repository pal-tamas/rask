using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rask.Cqrs;

/// <summary>
/// Remembers which event objects already have their durable handlers stored — by the outbox, in the transaction of the
/// save that raised them — so publishing the same object after the commit runs only its in-memory handlers.
/// </summary>
/// <remarks>Public only so the outbox package can mark what it stored; an application does not call it.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DurableEvents
{
    // By reference, never by value: two equal records raised twice are two events. Weak, so a mark never keeps an
    // event alive.
    private static readonly ConditionalWeakTable<IEvent, object> _stored = new();

    /// <summary>Records that <paramref name="e"/>'s durable handlers are stored.</summary>
    /// <param name="e">The event object that was stored.</param>
    public static void MarkStored(IEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        _stored.AddOrUpdate(e, _stored);
    }

    internal static bool Take(IEvent e) => _stored.Remove(e);
}
