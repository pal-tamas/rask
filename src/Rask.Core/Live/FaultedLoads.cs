using System.Runtime.CompilerServices;

namespace Rask.Core.Live;

/// <summary>
///     The components whose <c>OnMount</c> or <c>OnUpdated</c> faulted: each loads again the next time it is
///     rendered with its boundary recovered, so "Try again" tries the load and not only the drawing.
/// </summary>
/// <remarks>
///     Without it a recovered boundary showed the component as its failed load left it — on its placeholder,
///     with no error and nothing on the way. Kept off the component for the reason <see cref="HeldHookFault" />
///     is: a field there is paid by every node of every page, and this is written on a fault.
/// </remarks>
internal static class FaultedLoads
{
    private static readonly ConditionalWeakTable<Component, object> Faulted = [];

    // How many are noted. A render reads this once per component and nothing else while it is zero.
    private static int _noted;

    internal static bool Any => Volatile.Read(ref _noted) != 0;

    internal static void Note(Component component)
    {
        if (Faulted.TryAdd(component, Faulted))
        {
            Interlocked.Increment(ref _noted);
        }
    }

    internal static bool Take(Component component)
    {
        if (!Any || !Faulted.Remove(component))
        {
            return false;
        }

        Interlocked.Decrement(ref _noted);
        return true;
    }
}
