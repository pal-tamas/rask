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
/// <see cref="ModelBuilderExtensions.ApplyRaskConventions(Microsoft.EntityFrameworkCore.ModelBuilder)"/>.
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
            // Only what the entity actually maps: an append-only table declines UpdatedAt with a Stamps
            // const, and asking an entry for a property that is not mapped throws rather than no-ops.
            if (entry.State == EntityState.Added)
            {
                // Fill, not overwrite: an entity that stamped itself (Entity<TId>.Stamp) knows its own time
                // and keeps it. One that did not gets the framework's, so neither can end up at 0001-01-01.
                Stamp(entry, Columns.CreatedAt, now, onlyWhenUnset: true);
                Stamp(entry, Columns.UpdatedAt, now, onlyWhenUnset: true);
                StampTenant(entry);
                SyncTenantKey(entry);
            }
            else if (entry.State == EntityState.Modified)
            {
                Stamp(entry, Columns.UpdatedAt, now);
                RefuseTenantChange(entry);
                SyncTenantKey(entry);
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

    // Keeps TenantKey equal to TenantId with its null folded away, for the one table that maps it.
    //
    // NOT gated on the tenancy registry, deliberately: the accounts table cannot declare Scope = PerTenant,
    // because stamping a tenant-scoped row demands an ambient tenant and an ADMINISTRATOR legitimately has
    // none — a tenant-scoped accounts table could not have an admin inserted into it at all. So Rask.Auth
    // maps the tenant itself, and this keeps the indexed copy honest wherever it is mapped.
    private static void SyncTenantKey(EntityEntry entry)
    {
        if (entry.Metadata.FindProperty(Columns.TenantKey) is null ||
            entry.Metadata.FindProperty(Columns.TenantId) is null)
        {
            return;
        }

        entry.Property(Columns.TenantKey).CurrentValue =
            entry.Property(Columns.TenantId).CurrentValue as Guid? ?? Guid.Empty;
    }

    // A tenant-scoped row records its tenant on insert, from the ambient scope. The column is only mapped on
    // a table whose Scope const asked for it, so an unmapped one means the entity is not partitioned.
    //
    // Current.RequiredTenant throws when nothing is set — no explicit scope and no signed-in tenant, which is the whole point: writing a tenant-scoped row with
    // no tenant would otherwise store a NULL that every tenant's filter then excludes — a row nobody can read.
    private static void StampTenant(EntityEntry entry)
    {
        // The REGISTRY decides, not whether the column happens to be mapped. TenantId is a real property on
        // Entity<TId>, so EF Core's own convention maps it on any entity the conventions did not reach — a
        // context that maps a battery's tables AFTER ApplyRaskConventions, for one. Keying off the column
        // would then demand a tenant for a table that never asked to be partitioned.
        if (ConventionRegistry.ScopeFor(entry.Metadata.ClrType) != Tenancy.PerTenant ||
            entry.Metadata.FindProperty(Columns.TenantId) is null)
        {
            return;
        }

        var property = entry.Property(Columns.TenantId);

        // Already set deliberately — a cross-tenant tool creating a row on somebody's behalf inside
        // Tenant.Across(), or a test — is left alone.
        if (property.CurrentValue is Guid existing && existing != Guid.Empty)
        {
            return;
        }

        property.CurrentValue = Tenant.IsAcrossTenants
            ? throw new InvalidOperationException(
                $"'{entry.Metadata.ClrType.Name}' is tenant-scoped and is being inserted inside " +
                "Tenant.Across(), which says which tenant it belongs to for nobody. Set TenantId on the row, " +
                "or open Tenant.Use(id) around the insert.")
            : Current.RequiredTenant;
    }

    // A row does not move between tenants. The query filter already stops you LOADING another tenant's row,
    // so this catches the case the filter cannot: a row loaded inside Tenant.Across(), or one whose TenantId
    // was assigned in code, being saved into a different tenant than it was read from.
    private static void RefuseTenantChange(EntityEntry entry)
    {
        if (ConventionRegistry.ScopeFor(entry.Metadata.ClrType) != Tenancy.PerTenant ||
            entry.Metadata.FindProperty(Columns.TenantId) is null)
        {
            return;
        }

        var property = entry.Property(Columns.TenantId);

        if (property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
        {
            throw new InvalidOperationException(
                $"'{entry.Metadata.ClrType.Name}' would move from tenant {property.OriginalValue} to " +
                $"{property.CurrentValue}. A row belongs to the tenant it was created in; copy it into the " +
                "other tenant instead of reassigning it.");
        }
    }

    // Mapped or not. Both stamps are always on the CLR type — they come from Entity<TId> — so the MODEL is
    // what decides, and an entity that narrowed its Stamps leaves one unmapped.
    private static void Stamp(EntityEntry entry, string column, DateTime now, bool onlyWhenUnset = false)
    {
        if (entry.Metadata.FindProperty(column) is null)
        {
            return;
        }

        var property = entry.Property(column);

        if (onlyWhenUnset && property.CurrentValue is DateTime { } existing && existing != default)
        {
            return;
        }

        property.CurrentValue = now;
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
