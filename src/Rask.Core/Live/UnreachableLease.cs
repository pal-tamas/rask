using System.Runtime.CompilerServices;

namespace Rask.Core.Live;

/// <summary>
///     Lets a lease's hold go once nothing can reach the provider it was handed out with: no work is left that could
///     still use the event's services through it, wherever that work ran.
/// </summary>
/// <remarks>
///     <para>
///         Work a hook detaches comes back through the hook's context and lets its lease go there
///         (<see cref="HookLeases" />) — unless what it awaited was already done. That await goes on inline, on
///         whichever thread got there, and nothing comes back: work that has ended looks to the page exactly like work
///         that never returns, so the hold lasted until the component left. The provider tells the two apart: work
///         that can still use the event's services holds the provider it was lent, and once the collector finds it
///         unreachable no such work is left.
///     </para>
///     <para>
///         Kept as a weak table's value beside the lease: the lease keeps it alive, and it goes with the lease. Only a
///         hook that takes the provider itself pays for it.
///     </para>
/// </remarks>
internal sealed class UnreachableLease
{
    private static readonly ConditionalWeakTable<EventServicesLease, UnreachableLease> ByLease = new();

    private readonly EventHold _hold;

    private UnreachableLease(EventHold hold) => _hold = hold;

    /// <summary>Releases <paramref name="hold" /> once nothing can reach <paramref name="lease" /> any more.</summary>
    internal static void Watch(EventServicesLease lease, EventHold hold) => ByLease.Add(lease, new UnreachableLease(hold));

    // The finalizer is the runtime's one notice that nothing reaches the lease. The release takes the session's gate and
    // a hold's registration, so it runs on the pool rather than the finalizer thread. A hold let go already ignores it.
#pragma warning disable MA0055 // No other API tells a weak table's owner its key is gone: this owns no resource, it only lets a hold go.
    ~UnreachableLease() =>
        ThreadPool.UnsafeQueueUserWorkItem(static hold => hold.Release(), _hold, preferLocal: false);
#pragma warning restore MA0055
}
