using System.Runtime.CompilerServices;
using Rask.Core.Components;

namespace Rask.Core.Live;

/// <summary>
///     A lifecycle hook's fault that arrived before the render walk reached its component, kept until it does.
/// </summary>
/// <remarks>
///     <c>OnMount</c> and <c>OnUpdated</c> start where the parent's render ends, and the walk reaches the
///     component afterwards: only then is it known which boundary is around it. A hook that throws before its
///     first await, or whose read ends on another thread first, faults in between. The walk takes it up where
///     it meets the component and throws it there, so it reaches the boundary a fault of that component's
///     render reaches, in the same pass.
/// </remarks>
internal static class HeldHookFault
{
    // Off the component: a field there is paid by every node of every page, and this is read on a fault.
    private static readonly ConditionalWeakTable<Component, Exception> Held = [];

    // How many are held. The walk reads this once per component and nothing else while it is zero.
    private static int _held;

    // The fault this thread's walk is throwing, so the boundary that catches it knows a hook threw it.
    [ThreadStatic]
    private static Exception? t_thrown;

    internal static bool Any => Volatile.Read(ref _held) != 0;

    /// <summary>Test seam: whether a fault of <paramref name="component" /> is waiting for the walk.</summary>
    internal static bool Holds(Component component) => Held.TryGetValue(component, out _);

    internal static void Hold(Component component, Exception fault)
    {
        if (Held.TryAdd(component, fault))
        {
            Interlocked.Increment(ref _held);
        }
    }

    internal static Exception? Take(Component component)
    {
        if (!Any || !Held.Remove(component, out var fault))
        {
            return null;
        }

        Interlocked.Decrement(ref _held);
        return fault;
    }

    /// <summary>Marks <paramref name="fault" /> as a hook's, for the boundary the walk is about to throw it to.</summary>
    internal static void Throwing(Exception fault) => t_thrown = fault;

    /// <summary>Where <paramref name="caught" /> came from: a hook the walk threw for, or the render itself.</summary>
    internal static ErrorSource SourceOf(Exception caught)
    {
        var fromHook = ReferenceEquals(t_thrown, caught);
        t_thrown = null;
        return fromHook ? ErrorSource.Lifecycle : ErrorSource.Render;
    }
}
