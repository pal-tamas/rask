using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data;

/// <summary>
/// Makes deletion of an <see cref="ISoftDeletable"/> transparent: before each save, any entry marked
/// <see cref="EntityState.Deleted"/> is rewritten to <see cref="EntityState.Modified"/> with
/// <see cref="ISoftDeletable.DeletedAt"/> set to now, so <c>db.Remove(entity)</c> updates the row instead of
/// removing it. The global query filter added by <see cref="ModelBuilderExtensions.ApplyRaskConventions"/>
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

        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Property(nameof(ISoftDeletable.DeletedAt)).CurrentValue = now;
            }
        }
    }
}
