using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
/// Maintains the framework-owned columns before every save: stamps <c>CreatedAt</c> /
/// <c>UpdatedAt</c>, and bumps the <c>Version</c> concurrency token on
/// each update so the stored value changes (SQLite has no native rowversion). Registered by
/// <see cref="RaskDataServiceCollectionExtensions.AddRaskData"/> after the <see cref="SoftDeleteInterceptor"/>,
/// so a soft delete (rewritten to <see cref="EntityState.Modified"/>) is stamped and versioned too. It also
/// refuses an added <see cref="Model{TId}"/> whose non-integer key is still at its default — see
/// <see cref="ModelBuilderExtensions.ApplyRaskConventions"/>.
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

        RefuseUnassignedKeys(context);

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var entry in context.ChangeTracker.Entries<ITimestamped>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(Columns.CreatedAt).CurrentValue = now;
                entry.Property(Columns.UpdatedAt).CurrentValue = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(Columns.UpdatedAt).CurrentValue = now;
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<IVersioned>())
        {
            if (entry.State == EntityState.Modified)
            {
                var version = entry.Property(Columns.Version);
                version.CurrentValue = (int)(version.CurrentValue ?? 0) + 1;
            }
        }
    }

    // A Model<TId> key that is not an integer is the entity's to assign (see ApplyRaskConventions), so nothing
    // fills one left at its default. Inserting it would write an empty key — the first row lands and the second
    // collides — and the author would meet that as a duplicate-key error far from the factory that forgot the id.
    private static void RefuseUnassignedKeys(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added ||
                entry.Entity is not Model ||
                ModelBuilderExtensions.IdTypeOf(entry.Metadata.ClrType) is not { } idType ||
                ModelBuilderExtensions.IsInteger(idType) ||
                entry.Metadata.FindProperty(nameof(Model<int>.Id)) is not { ValueGenerated: ValueGenerated.Never } key)
            {
                continue;
            }

            if (Equals(entry.Property(key.Name).CurrentValue, key.Sentinel))
            {
                throw new InvalidOperationException(
                    $"'{entry.Metadata.ClrType.Name}' was added with its Id still at the default, and a " +
                    $"Model<{idType.Name}> assigns its own id — nothing generates one. Set it where the entity is " +
                    "created (for a Guid key, Guid.CreateVersion7()), or save it through the generated CreateAsync: " +
                    "CreateAsync(model) assigns a Guid key and CreateAsync(id, model) takes yours.");
            }
        }
    }
}
