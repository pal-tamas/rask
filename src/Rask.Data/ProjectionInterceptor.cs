using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
///     Raises each declared projection's event (<c>b.Projects&lt;E&gt;(…)</c>) in the save that changed its rows: once per
///     save, onto the first changed row, before the outbox and the domain-event publish read the rows' events — so a
///     durable handler's row commits with the change, an in-memory handler hears it after the commit, and a rolled-back
///     save raises nothing.
/// </summary>
/// <remarks>
///     A save that fails leaves its events on their rows, as every domain event is left, and a rollback puts them back;
///     so a projection's own event is first taken back off every row it could ride on and made again from what the save
///     now changes — a retried save raises it once, and a row saved later with nothing watched changed raises none.
/// </remarks>
internal sealed class ProjectionInterceptor : SaveChangesInterceptor
{
    internal static readonly ProjectionInterceptor Instance = new();

    // By reference: the events a projection made, so only those are taken back, never one the aggregate raised itself.
    private static readonly ConditionalWeakTable<IEvent, object> Projected = new();

    private static readonly Predicate<IEvent> IsProjected = static e => Projected.TryGetValue(e, out _);

    private ProjectionInterceptor()
    {
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Raise(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Raise(eventData.Context);
        return new(result);
    }

    private static void Raise(DbContext? context)
    {
        if (context is null || !DeclaredProjections.Any)
        {
            return;
        }

        Dictionary<DeclaredProjection, List<EntityEntry>>? changed = null;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            var projections = DeclaredProjections.Of(entry.Metadata.ClrType);
            if (projections.IsEmpty)
            {
                continue;
            }

            if (entry.Entity is IProjectionCarrier carrier)
            {
                carrier.Withdraw(IsProjected);
            }

            foreach (var projection in projections.Where(p => p.Sees(entry)))
            {
                changed ??= [];
                if (!changed.TryGetValue(projection, out var entries))
                {
                    changed[projection] = entries = [];
                }

                entries.Add(entry);
            }
        }

        if (changed is null)
        {
            return;
        }

        // After the walk, so no app code runs while the tracker is enumerated.
        foreach (var (projection, entries) in changed)
        {
            if (projection.Raise(entries) is { } e)
            {
                Projected.AddOrUpdate(e, Projected);
                ((IProjectionCarrier)entries[0].Entity).Raise(e);
            }
        }
    }
}
