using System.Runtime.CompilerServices;

namespace Rask.Core.Live;

/// <summary>
///     The leases a lifecycle hook took on its event's services, until the work it detached comes back.
/// </summary>
/// <remarks>
///     <para>
///         A hook that detaches its slow part — <c>_ = Run();</c>, a task that reads through the provider it took — is
///         done long before that work is. The work comes back to the page through the hook's own context (its await
///         resumes there), so the first continuation the context runs once the hook itself has finished is the
///         detached work returning: the leases taken before it are let go once it has run. Work that never comes back
///         lets them go when its component has left the page, after the grace, or with the session.
///     </para>
///     <para>
///         Kept beside the hook's context, not on it: a context is made for every hook a render runs, and a field there
///         would cost every one of them for what a few ever ask.
///     </para>
/// </remarks>
internal static class HookLeases
{
    private static readonly ConditionalWeakTable<LifecycleSyncContext, Taken> ByHook = new();

    // Whether any hook has ever taken a lease: until one has, no hook pays for a lookup.
    private static volatile bool _any;

    /// <summary>Holds <paramref name="hold" /> until the work <paramref name="hook" /> detached comes back.</summary>
    internal static void Add(LifecycleSyncContext hook, EventHold hold)
    {
        _any = true;
        var taken = ByHook.GetValue(hook, static _ => new Taken());
        lock (taken)
        {
            taken.Holds.Add(hold);
        }
    }

    /// <summary>
    ///     As a continuation through <paramref name="hook" /> starts: the leases it lets go once it has run — those
    ///     taken before it, when the hook itself is done — or null.
    /// </summary>
    internal static List<EventHold>? Returning(LifecycleSyncContext hook)
    {
        if (!_any || !hook.HookDone || !ByHook.TryGetValue(hook, out var taken))
        {
            return null;
        }

        lock (taken)
        {
            if (taken.Holds.Count == 0)
            {
                return null;
            }

            var back = taken.Holds;
            taken.Holds = [];
            return back;
        }
    }

    private sealed class Taken
    {
        public List<EventHold> Holds { get; set; } = [];
    }
}
