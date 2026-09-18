using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var entry in context.ChangeTracker.Entries<IAggregate>())
        {
            // Only an aggregate that asked for soft delete has the column. The default is a real delete, so
            // the absence of DeletedAt is the answer rather than something to work around.
            if (entry.State == EntityState.Deleted && entry.Metadata.FindProperty(Columns.DeletedAt) is not null)
            {
                // Unchanged first, then the one column. Setting Modified marks EVERY property modified, so the UPDATE
                // wrote back every column the deleting context had loaded — and a delete of a row someone else had
                // changed since silently reverted their change (#1055). The entry still ends up Modified, which is
                // what AuditingInterceptor reacts to when it adds UpdatedAt and Version.
                entry.State = EntityState.Unchanged;
                var deletedAt = entry.Property(Columns.DeletedAt);
                deletedAt.CurrentValue = now;
                deletedAt.IsModified = true;
            }
        }
    }
}
