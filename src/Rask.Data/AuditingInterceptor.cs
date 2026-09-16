using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
/// Maintains the framework-owned columns before every save: stamps <c>CreatedAt</c> /
/// <c>UpdatedAt</c>, and bumps the <c>Version</c> concurrency token on
/// each update so the stored value changes (SQLite has no native rowversion). Registered by
/// <see cref="RaskDataServiceCollectionExtensions.AddRaskData"/> after the <see cref="SoftDeleteInterceptor"/>,
/// so a soft delete (rewritten to <see cref="EntityState.Modified"/>) is stamped and versioned too. It also
/// refuses an added <see cref="Aggregate{TId}"/> whose non-integer key is still at its default — see
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
        TouchRootsOfChangedChildren(context);

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var entry in context.ChangeTracker.Entries<IEntity>())
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

        foreach (var entry in context.ChangeTracker.Entries<IAggregate>())
        {
            if (entry.State == EntityState.Modified)
            {
                var version = entry.Property(Columns.Version);
                version.CurrentValue = (int)(version.CurrentValue ?? 0) + 1;
            }
        }
    }

    /// <summary>
    /// Marks the root of every changed child as changed too, so the aggregate is the unit of concurrency.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A line's quantity changing IS the order changing: the root's <c>UpdatedAt</c> becomes true again, and its
    /// <c>Version</c> moves, so a caller holding the version it read is refused — which is what makes
    /// <c>UpdateAsync(id, version, …)</c> protect the whole aggregate rather than only the root's own columns.
    /// </para>
    /// <para>
    /// Only those two columns are flagged, never <c>State = Modified</c>. Marking the entry modified marks EVERY
    /// property modified, so the UPDATE would write back every column this context had loaded and quietly revert a
    /// change another writer made since (#1055). The same reasoning as <see cref="SoftDeleteInterceptor" />.
    /// </para>
    /// <para>
    /// Children are found by walking up their foreign key rather than down the root's collection, because a child
    /// being deleted has usually been removed from that collection already.
    /// </para>
    /// </remarks>
    private static void TouchRootsOfChangedChildren(DbContext context)
    {
        // Materialised: flagging a property below moves an entry to Modified, and the tracker cannot be mutated
        // while it is being enumerated.
        var changedChildren = context.ChangeTracker.Entries()
            .Where(static e => e.Entity is IEntity and not IAggregate
                               && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var child in changedChildren)
        {
            if (RootOf(context, child) is not { State: EntityState.Unchanged } root)
            {
                continue;
            }

            root.Property(Columns.UpdatedAt).IsModified = true;
            root.Property(Columns.Version).IsModified = true;
        }
    }

    /// <summary>The aggregate <paramref name="entry" /> belongs to, walking up through nested children.</summary>
    private static EntityEntry? RootOf(DbContext context, EntityEntry entry, int depth = 0)
    {
        // The same ceiling AggregateChildren walks down with: an aggregate deeper than this is several
        // aggregates wearing one name, and the limit makes a cycle impossible to loop on.
        if (depth >= 4)
        {
            return null;
        }

        foreach (var foreignKey in entry.Metadata.GetForeignKeys())
        {
            if (!typeof(IEntity).IsAssignableFrom(foreignKey.PrincipalEntityType.ClrType) ||
                PrincipalOf(context, entry, foreignKey) is not { } principal)
            {
                continue;
            }

            return principal.Entity is IAggregate ? principal : RootOf(context, principal, depth + 1);
        }

        return null;
    }

    // By key values rather than through a navigation: a child mapped with a shadow foreign key and no inverse
    // navigation — which is the shape Rask recommends — has no reference to read.
    private static EntityEntry? PrincipalOf(DbContext context, EntityEntry child, IForeignKey foreignKey)
    {
        var values = new object?[foreignKey.Properties.Count];

        for (var i = 0; i < values.Length; i++)
        {
            var property = child.Property(foreignKey.Properties[i].Name);

            // A child being deleted has had its foreign key cleared, so what it USED to point at is the answer.
            values[i] = property.CurrentValue ?? property.OriginalValue;

            if (values[i] is null)
            {
                return null;
            }
        }

        foreach (var candidate in context.ChangeTracker.Entries())
        {
            if (!foreignKey.PrincipalEntityType.ClrType.IsInstanceOfType(candidate.Entity))
            {
                continue;
            }

            var matches = true;

            for (var i = 0; i < values.Length && matches; i++)
            {
                matches = Equals(candidate.Property(foreignKey.PrincipalKey.Properties[i].Name).CurrentValue, values[i]);
            }

            if (matches)
            {
                return candidate;
            }
        }

        return null;
    }

    // An Entity<TId> key that is not an integer is the entity's to assign (see ApplyRaskConventions), so nothing
    // fills one left at its default. Inserting it would write an empty key — the first row lands and the second
    // collides — and the author would meet that as a duplicate-key error far from the factory that forgot the id.
    private static void RefuseUnassignedKeys(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added ||
                entry.Entity is not IEntity ||
                ModelBuilderExtensions.IdTypeOf(entry.Metadata.ClrType) is not { } idType ||
                ModelBuilderExtensions.IsInteger(idType) ||
                entry.Metadata.FindProperty(nameof(Entity<int>.Id)) is not { ValueGenerated: ValueGenerated.Never } key)
            {
                continue;
            }

            if (Equals(entry.Property(key.Name).CurrentValue, key.Sentinel))
            {
                throw new InvalidOperationException(
                    $"'{entry.Metadata.ClrType.Name}' was added with its Id still at the default, and a " +
                    $"Entity<{idType.Name}> assigns its own id — nothing generates one. Set it where the entity is " +
                    "created — for a Guid key, Guid.CreateVersion7() in the entity's constructor or factory.");
            }
        }
    }
}
