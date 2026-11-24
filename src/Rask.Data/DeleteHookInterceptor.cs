using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data;

/// <summary>
///     Calls the <c>OnDelete()</c> of every aggregate a save deletes, before soft-delete rewrites the removal and before
///     the outbox and the domain-event publish read the rows' events — so what the hook raises is stored, published after
///     the commit and never on a rollback, exactly as an operation's <c>Raise</c>.
/// </summary>
/// <remarks>
///     Once per aggregate instance: a failed save leaves the raised events on the aggregate (and a rollback puts them
///     back), so the retry carries them without asking the hook again.
/// </remarks>
internal sealed class DeleteHookInterceptor : SaveChangesInterceptor
{
    internal static readonly DeleteHookInterceptor Instance = new();

    // By reference, weak: the instances whose hook already ran.
    private static readonly ConditionalWeakTable<object, object> Called = new();

    private DeleteHookInterceptor()
    {
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        CallHooks(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        CallHooks(eventData.Context);
        return new(result);
    }

    private static void CallHooks(DbContext? context)
    {
        if (context is null || !DeleteHooks.Any)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Deleted &&
                DeleteHooks.Of(entry.Metadata.ClrType) is { } hook &&
                Called.TryAdd(entry.Entity, Called))
            {
                hook(entry.Entity);
            }
        }
    }
}
