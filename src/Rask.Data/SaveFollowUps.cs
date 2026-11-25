using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     Work a save leaves for once its rows are written and before its transaction commits: a numbering kept. Run by
///     <see cref="WritableChildrenInterceptor" /> just before it ends the transaction <see cref="SaveTransaction" /> opened for the save — or inside the caller's own.
/// </summary>
internal static class SaveFollowUps
{
    private static readonly ConditionalWeakTable<DbContext, List<Func<CancellationToken, Task>>> Pending = [];

    /// <summary>Leaves <paramref name="work" /> for the end of <paramref name="context" />'s save in progress.</summary>
    internal static void Add(DbContext context, Func<CancellationToken, Task> work) =>
        Pending.GetOrCreateValue(context).Add(work);

    /// <summary>Runs what the save left, in the order it was left.</summary>
    internal static async Task Run(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null || !Pending.TryGetValue(context, out var work))
        {
            return;
        }

        Pending.Remove(context);
        foreach (var step in work)
        {
            await step(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Forgets what a save that failed, or never started, left.</summary>
    internal static void Forget(DbContext? context)
    {
        if (context is not null)
        {
            Pending.Remove(context);
        }
    }
}
