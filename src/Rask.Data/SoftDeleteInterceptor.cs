using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
/// Makes deletion of an <see cref="Aggregate{TId}"/> transparent: before each save, any entry marked
/// <see cref="EntityState.Deleted"/> is rewritten to an update of <c>DeletedAt</c> alone, set to now, so
/// <c>db.Remove(entity)</c> updates the row instead of removing it — and writes no other column back, so a
/// delete never reverts a change another writer made since the entity was loaded. The global query filter added by <see cref="ModelBuilderExtensions.ApplyRaskConventions(Microsoft.EntityFrameworkCore.ModelBuilder)"/>
/// then hides it. Runs before the <see cref="AuditingInterceptor"/> so the soft delete is also timestamped
/// and versioned.
/// </summary>
public sealed class SoftDeleteInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // First in line, before the loop below detects changes: a refilled collection would fail there.
        ReplacedChildren.Match(context);

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Materialised: a kept child puts back what it holds, which changes the tracker's entries.
        foreach (var entry in context.ChangeTracker.Entries<IEntity>().ToList())
        {
            // Only an aggregate that asked for soft delete, or a child that says ISoftDeletes, has the column. The
            // default is a real delete, so the absence of DeletedAt is the answer rather than something to work around.
            if (entry.State == EntityState.Deleted && entry.Metadata.FindProperty(Columns.DeletedAt) is not null &&
                (entry.Entity is IAggregate || ConventionRegistry.Has(entry.Metadata.ClrType, TraitSet.SoftRemoved)))
            {
                Stamp(entry, now);

                // A child its aggregate let go of keeps its parent and what it holds: letting go had severed the one and
                // the cascade had deleted the others.
                if (entry.Entity is not IAggregate)
                {
                    KeepParent(entry);
                    KeepHeld(context, entry);
                }
            }
        }
    }

    // Unchanged first, then the one column. Setting Modified marks EVERY property modified, so the UPDATE wrote back
    // every column the deleting context had loaded — and a delete of a row someone else had changed since silently
    // reverted their change (#1055). The entry still ends up Modified, which is what AuditingInterceptor reacts to
    // when it adds UpdatedAt and Version.
    private static void Stamp(EntityEntry entry, DateTime now)
    {
        entry.State = EntityState.Unchanged;
        var deletedAt = entry.Property(Columns.DeletedAt);
        deletedAt.CurrentValue = TimestampColumn.Of(deletedAt.Metadata.ClrType, now);
        deletedAt.IsModified = true;
    }

    // Removed from its parent's collection, the child's key to it was cleared: the row still belongs where it did.
    private static void KeepParent(EntityEntry entry)
    {
        foreach (var key in entry.Properties.Where(static p => p.Metadata.IsForeignKey()))
        {
            key.CurrentValue = key.OriginalValue;
            key.IsModified = false;
        }
    }

    // The rows a kept child holds were deleted with it — by the cascade, or by a save that let go of its whole graph —
    // and stay as they were, at any depth. Found by their key to it, since the copy deleted may hold none of them.
    private static void KeepHeld(DbContext context, EntityEntry kept, int depth = 0)
    {
        if (depth > 4)
        {
            return;
        }

        foreach (var key in kept.Metadata.GetReferencingForeignKeys().Where(static k => k.PrincipalToDependent is { IsCollection: true }))
        {
            var principal = key.PrincipalKey.Properties.Select(p => kept.Property(p.Name).CurrentValue).ToArray();

            foreach (var held in context.ChangeTracker.Entries().ToList())
            {
                if (held.State == EntityState.Deleted &&
                    key.DeclaringEntityType.IsAssignableFrom(held.Metadata) &&
                    held.Metadata.FindProperty(Columns.DeletedAt) is null &&
                    key.Properties.Select(p => held.Property(p.Name).OriginalValue).SequenceEqual(principal))
                {
                    held.State = EntityState.Unchanged;
                    KeepParent(held);
                    KeepHeld(context, held, depth + 1);
                }
            }
        }
    }
}
