using System.Runtime.CompilerServices;

namespace Rask.Core.Live;

/// <summary>
///     The mounted components of this process's live pages that handle a published event (they implement
///     <c>IEventHandler&lt;T&gt;</c>), and the delivery that hands each one the event in its own session.
/// </summary>
/// <remarks>
///     Weakly held, so a page that went without unmounting keeps nothing alive. An app none of whose components handle
///     an event never gets past <see cref="LiveEvents.Any" />, so mounting and unmounting cost it one read.
/// </remarks>
internal static class LiveListeners
{
    private static readonly ConditionalWeakTable<Component, LiveEvents.Listener[]> Listening = new();

    /// <summary>A component has mounted: one that handles an event starts hearing it.</summary>
    internal static void Mounted(Component component)
    {
        if (LiveEvents.Any && LiveEvents.Of(component.GetType()) is { } listeners)
        {
            LiveEvents.Deliver ??= Deliver;
            Listening.AddOrUpdate(component, listeners);
        }
    }

    /// <summary>A component has unmounted: it hears nothing more.</summary>
    internal static void Unmounted(Component component)
    {
        if (LiveEvents.Any)
        {
            Listening.Remove(component);
        }
    }

    private static void Deliver(object e)
    {
        foreach (var (component, listeners) in Listening)
        {
            if (component.IsTornDown || component.RenderHandle is not LiveSessionBase session)
            {
                continue;
            }

            foreach (var listener in listeners.Where(listener => listener.Event == e.GetType()))
            {
                session.DeliverLiveEvent(component, listener.Handle, e);
            }
        }
    }
}
