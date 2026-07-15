using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data;

/// <summary>
/// Maintains the framework-owned columns before every save: stamps <see cref="ITimestamped.CreatedAt"/> /
/// <see cref="ITimestamped.UpdatedAt"/>, and bumps the <see cref="IVersioned.Version"/> concurrency token on
/// each update so the stored value changes (SQLite has no native rowversion). Registered by
/// <see cref="RaskDataServiceCollectionExtensions.AddRaskData"/> after the <see cref="SoftDeleteInterceptor"/>,
/// so a soft delete (rewritten to <see cref="EntityState.Modified"/>) is stamped and versioned too.
/// </summary>
public sealed class AuditingInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
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

        foreach (var entry in context.ChangeTracker.Entries<ITimestamped>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(ITimestamped.CreatedAt)).CurrentValue = now;
                entry.Property(nameof(ITimestamped.UpdatedAt)).CurrentValue = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(ITimestamped.UpdatedAt)).CurrentValue = now;
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<IVersioned>())
        {
            if (entry.State == EntityState.Modified)
            {
                var version = entry.Property(nameof(IVersioned.Version));
                version.CurrentValue = (int)(version.CurrentValue ?? 0) + 1;
            }
        }
    }
}
