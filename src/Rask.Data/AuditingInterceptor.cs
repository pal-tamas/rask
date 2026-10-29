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
/// refuses an added <see cref="Aggregate{TId}"/> whose never-generated key is still at its default — see
/// <see cref="ModelBuilderExtensions.ApplyRaskConventions(Microsoft.EntityFrameworkCore.ModelBuilder)"/>.
/// </summary>
public sealed class AuditingInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        // The same work as the async path. A synchronous save has no other way to ask about a parent it does not hold.
        if (eventData.Context is { } context)
        {
            ReplacedChildren.Match(context);
            SeveredChildren.Delete(context);
            ChildWrites.RefuseOutsideTheirAggregate(context);
            ParentTenantCheck.RefuseAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        }

        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc/>
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            ReplacedChildren.Match(context);
            SeveredChildren.Delete(context);
            ChildWrites.RefuseOutsideTheirAggregate(context);
            await ParentTenantCheck.RefuseAsync(context, cancellationToken).ConfigureAwait(false);
        }

        Apply(eventData.Context);
        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        RefuseTenantWritesForNobody(context);
        RefuseUnassignedKeys(context);
        TouchRootsOfChangedChildren(context);
        RefuseRowsOfAnotherTenant(context);

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
                StampTenant(context, entry);
                RefuseChildApartFromItsRoot(context, entry);
                SyncTenantKey(entry);
            }
            else if (entry.State == EntityState.Modified)
            {
                Stamp(entry, Columns.UpdatedAt, now);
                RefuseTenantChange(entry);
                RefuseChildApartFromItsRoot(context, entry);
                SyncTenantKey(entry);
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<IAggregate>())
        {
            // Only an aggregate that maps the token: one that does not name Versioned has no Version,
            // and asking an entry for a property that is not mapped throws rather than no-ops.
            if (entry.State == EntityState.Modified && entry.Metadata.FindProperty(Columns.Version) is not null)
            {
                var version = entry.Property(Columns.Version);
                version.CurrentValue = (int)(version.CurrentValue ?? 0) + 1;
            }
        }
    }

    // An app that registered a tenant resolver reads a tenant-scoped table as EMPTY when the resolver named no
    // tenant, and that is only safe if the same work cannot write one: an insert would store a row for nobody,
    // and an update or a delete could only be of a row loaded under some other tenant. Every kind of write is
    // refused, whether or not the row already carries a TenantId — the work has no tenant to act for.
    private static void RefuseTenantWritesForNobody(DbContext context)
    {
        if (!Tenant.IsUnresolved)
        {
            return;
        }

        var written = context.ChangeTracker.Entries<IEntity>().FirstOrDefault(static entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
            ConventionRegistry.IsPerTenant(entry.Metadata.ClrType));

        if (written is not null)
        {
            throw MissingTenantException.ForWrite(written.Metadata.ClrType.Name);
        }
    }

    // A row is written in the tenant it was loaded in. The filter stops a read from reaching another tenant's
    // row, and says nothing about a row read EARLIER: an aggregate a page holds, or one a context still tracks,
    // saved after the tenant in flight became another. So every row about to be written that already has a
    // tenant — modified, deleted, or added with its TenantId set — must have the one in flight. Tenant.Across()
    // is the one deliberate way round, as it is for a read.
    private static void RefuseRowsOfAnotherTenant(DbContext context)
    {
        if (Tenant.IsAcrossTenants)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<IEntity>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                OwnerWithATenant(context, entry) is { } owner)
            {
                RefuseUnlessInFlight(entry, owner);
            }
        }
    }

    // The row whose tenant says where `entry` belongs: itself, or — for a child with no tenant in its column (added to
    // an aggregate that was loaded, or a legacy row) — its root. Null when nothing has one yet.
    private static EntityEntry? OwnerWithATenant(DbContext context, EntityEntry entry)
    {
        if (!IsPartitioned(entry))
        {
            return null;
        }

        // A child's own column is data its root stamps, NULL on a legacy row: with none, its root answers for it.
        if (HasTenant(entry) || (entry.State != EntityState.Added && entry.Entity is IAggregate))
        {
            return entry;
        }

        return entry.Entity is not IAggregate && RootOf(context, entry) is { State: not EntityState.Added } root && IsPartitioned(root)
            ? root
            : null;
    }

    private static void RefuseUnlessInFlight(EntityEntry entry, EntityEntry owner)
    {
        var column = owner.Property(Columns.TenantId);
        var rows = owner.State == EntityState.Added ? column.CurrentValue : column.OriginalValue;
        var table = entry.Metadata.ClrType.Name;

        // In the column's own type, as the stamp is: a numbered tenant is compared as the number.
        if (Tenant.Key is not { } tenant)
        {
            throw new InvalidOperationException(
                $"'{table}' belongs to tenant {rows ?? "none"} and is being saved with no tenant in flight. A row " +
                "belongs to the tenant it was loaded in; save it inside Tenant.Use(id), or inside Tenant.Across() " +
                "when crossing tenants is the point.");
        }

        var inFlight = TenantColumn.ValueFor(tenant, column.Metadata.ClrType, table);

        if (!Equals(rows, inFlight))
        {
            throw new InvalidOperationException(
                $"'{table}' belongs to tenant {rows ?? "none"} and is being saved in tenant {inFlight}. A row " +
                "belongs to the tenant it was loaded in; save it there, or inside Tenant.Across() when crossing " +
                "tenants is the point.");
        }
    }

    // The REGISTRY decides, as it does for the stamp: a table that is not partitioned may keep a TenantId of its own.
    private static bool IsPartitioned(EntityEntry entry) =>
        ConventionRegistry.IsPerTenant(entry.Metadata.ClrType) &&
        entry.Metadata.FindProperty(Columns.TenantId) is not null;

    private static bool HasTenant(EntityEntry entry) =>
        entry.Property(Columns.TenantId).CurrentValue is { } tenant && !Equals(tenant, Guid.Empty);

    // Keeps TenantKey equal to TenantId with its null folded away, for the one table that maps it.
    //
    // NOT gated on the tenancy registry, deliberately: the accounts table cannot name PerTenant,
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
    // Tenant.RequiredKey throws when nothing is set — no explicit scope and no signed-in tenant, which is the whole point: writing a tenant-scoped row with
    // no tenant would otherwise store a NULL that every tenant's filter then excludes — a row nobody can read.
    private static void StampTenant(DbContext context, EntityEntry entry)
    {
        // The REGISTRY decides, not whether the column happens to be mapped. A table that is not partitioned
        // may declare a TenantId of its own — a queue's rows record the tenant they were enqueued for — and
        // keying off the column would then demand a tenant for a table that never asked to be partitioned.
        if (!ConventionRegistry.IsPerTenant(entry.Metadata.ClrType) ||
            entry.Metadata.FindProperty(Columns.TenantId) is null)
        {
            return;
        }

        var property = entry.Property(Columns.TenantId);

        // Already set deliberately — a cross-tenant tool creating a row on somebody's behalf inside
        // Tenant.Across(), or a test — is left alone.
        if (property.CurrentValue is { } existing && !Equals(existing, Guid.Empty))
        {
            return;
        }

        // A child that keeps a column of its own takes its ROOT's tenant: the aggregate decides where its parts
        // belong, inside Tenant.Across() as well. Only a root not stamped yet leaves it to the tenant in flight.
        if (RootTenant(context, entry) is { } rootTenant)
        {
            property.CurrentValue = TenantColumn.ValueFor(rootTenant, property.Metadata.ClrType, entry.Metadata.ClrType.Name);
            return;
        }

        // In the column's own type: a table whose tenants are numbered is stamped with the number.
        property.CurrentValue = Tenant.IsAcrossTenants
            ? throw new InvalidOperationException(
                $"'{entry.Metadata.ClrType.Name}' is tenant-scoped and is being inserted inside " +
                "Tenant.Across(), which says which tenant it belongs to for nobody. Set TenantId on the row, " +
                "or open Tenant.Use(id) around the insert.")
            : TenantColumn.ValueFor(Tenant.RequiredKey, property.Metadata.ClrType, entry.Metadata.ClrType.Name);
    }

    // A child that keeps a column of its own agrees with its root: a part of one tenant's aggregate is that
    // tenant's row, whatever its column was set to in code.
    private static void RefuseChildApartFromItsRoot(DbContext context, EntityEntry entry)
    {
        if (entry.Entity is IAggregate || !IsPartitioned(entry) || RootTenant(context, entry) is not { } rootTenant)
        {
            return;
        }

        var property = entry.Property(Columns.TenantId);
        var table = entry.Metadata.ClrType.Name;

        // A legacy row that was loaded with no tenant of its own and still has none: it belongs to its root's tenant
        // through its parent, which the parent check and the filter already hold it to.
        if (entry.State == EntityState.Modified && property.CurrentValue is null && property.OriginalValue is null)
        {
            return;
        }

        if (!Equals(property.CurrentValue, TenantColumn.ValueFor(rootTenant, property.Metadata.ClrType, table)))
        {
            throw new InvalidOperationException(
                $"'{table}' is saved with tenant {property.CurrentValue ?? "none"}, and the aggregate it is part of " +
                $"belongs to tenant {rootTenant}. A child belongs to its root's tenant; leave its TenantId to Rask.");
        }
    }

    // The tenant of the aggregate a child is part of, as a Guid: null for a root, for a child whose root the
    // context does not hold, and for a root not stamped yet.
    private static Guid? RootTenant(DbContext context, EntityEntry entry)
    {
        if (entry.Entity is IAggregate ||
            RootOf(context, entry) is not { } root ||
            root.Metadata.FindProperty(Columns.TenantId) is null)
        {
            return null;
        }

        return root.Property(Columns.TenantId).CurrentValue switch
        {
            Guid tenant when tenant != Guid.Empty => tenant,
            int number => TenantNumber.ToGuid(number),
            long number => TenantNumber.ToGuid(number),
            _ => null,
        };
    }

    // A row does not move between tenants. The query filter already stops you LOADING another tenant's row,
    // so this catches the case the filter cannot: a row loaded inside Tenant.Across(), or one whose TenantId
    // was assigned in code, being saved into a different tenant than it was read from.
    private static void RefuseTenantChange(EntityEntry entry)
    {
        if (!ConventionRegistry.IsPerTenant(entry.Metadata.ClrType) ||
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
    /// <c>Update(id, version, …)</c> protect the whole aggregate rather than only the root's own columns.
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

            Touch(root, Columns.UpdatedAt);
            Touch(root, Columns.Version);
        }
    }

    // A root that declined its stamps or its version has nothing to mark: the child's own row is the whole write.
    private static void Touch(EntityEntry root, string column)
    {
        if (root.Metadata.FindProperty(column) is not null)
        {
            root.Property(column).IsModified = true;
        }
    }

    /// <summary>The aggregate <paramref name="entry" /> belongs to, walking up through nested children.</summary>
    internal static EntityEntry? RootOf(DbContext context, EntityEntry entry, int depth = 0)
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

    // An Entity<TId> key that is never generated is the entity's to assign — every key that is not an integer
    // (see ApplyRaskConventions), and an integer one the application configured so — and nothing fills one left
    // at its default. Inserting it would write an empty key — the first row lands and the second collides — and
    // the author would meet that as a duplicate-key error far from the factory that forgot the id.
    private static void RefuseUnassignedKeys(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added ||
                entry.Entity is not IEntity ||
                ModelBuilderExtensions.IdTypeOf(entry.Metadata.ClrType) is not { } idType ||
                entry.Metadata.FindProperty(nameof(Entity<int>.Id)) is not { ValueGenerated: ValueGenerated.Never } key)
            {
                continue;
            }

            if (Equals(entry.Property(key.Name).CurrentValue, key.Sentinel))
            {
                throw new InvalidOperationException(
                    $"'{entry.Metadata.ClrType.Name}' was added with its Id still at the default, and a " +
                    $"Entity<{idType.Name}> assigns its own id — nothing generates one. " +
                    (ModelBuilderExtensions.IsInteger(idType)
                        ? "Its key is configured ValueGeneratedNever(), so give it one: Create(id, …), or set it where the entity is built."
                        : "Set it where the entity is created — for a Guid key, Guid.CreateVersion7() in the entity's constructor or factory."));
            }
        }
    }
}
