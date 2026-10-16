using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     Refuses a child written on its own: a part of an aggregate is added, changed and removed only through its
///     loaded aggregate, so the save that writes it also holds its root.
/// </summary>
/// <remarks>
///     The root is what guards the aggregate's invariants, carries its <c>Version</c> and its <c>UpdatedAt</c>, and
///     decides its tenant. A child attached and saved alone skips every one of those. <see cref="Tenant.Across" /> does
///     not lift this: it is about the aggregate's shape, not about whose rows are visible.
/// </remarks>
internal static class ChildWrites
{
    // A row a save of its aggregate let go, staged for deletion as a detached copy whose parent key nobody kept.
    private static readonly ConditionalWeakTable<object, object> LetGoByTheirRoot = [];

    /// <summary>Marks <paramref name="child" /> as deleted by a save of its own aggregate.</summary>
    internal static void LetGoByItsRoot(object child) => LetGoByTheirRoot.AddOrUpdate(child, child);

    /// <summary>Throws for the first child changed in this save whose aggregate root the context does not hold.</summary>
    internal static void RefuseOutsideTheirAggregate(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries<IEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted) ||
                entry.Entity is IAggregate ||
                LetGoByTheirRoot.TryGetValue(entry.Entity, out _) ||
                ParentsOf(entry.Metadata) is not { Count: > 0 } parents ||
                AuditingInterceptor.RootOf(context, entry) is not null)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"'{entry.Metadata.ClrType.Name}' was {Verb(entry.State)} on its own, but a child is written only through "
                + $"its aggregate: load its {string.Join(" or ", parents.Select(static p => $"'{p}'"))}, change it there, "
                + "and save that.");
        }
    }

    /// <summary>
    ///     Throws for the first row keyed by several columns this save would write: such a row is read through its
    ///     aggregate — <c>DeliveryNoteTransaction.Where(…)</c>, an <c>Include</c> — and writing it is not supported yet.
    /// </summary>
    internal static void RefuseCompositeKeys(DbContext context)
    {
        var written = context.ChangeTracker.Entries<IEntity>().FirstOrDefault(static entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
            ModelBuilderExtensions.HasCompositeKey(entry.Metadata.ClrType));

        if (written is not null)
        {
            throw new NotSupportedException(
                $"'{written.Metadata.ClrType.Name}' was {Verb(written.State)}, but a row keyed by several columns is "
                + "read-only for now: read it through its read face, and write the table the way it is written today.");
        }
    }

    /// <summary>Throws when <paramref name="entity" /> is a child: a bulk load inserts roots, which bring their children.</summary>
    internal static void RefuseBulkInsert(DbContext context, [DynamicallyAccessedMembers(DataTrimming.Entity)] Type entity)
    {
        if (context.Model.FindEntityType(entity) is { } type && ParentsOf(type) is { Count: > 0 } parents)
        {
            throw new InvalidOperationException(
                $"BulkInsert of '{entity.Name}' inserts children on their own, but a child is written only through its "
                + $"aggregate: bulk insert the {string.Join(" or ", parents.Select(static p => $"'{p}'"))} holding them.");
        }
    }

    // The entities whose collection holds this one: what makes it a child rather than a table of its own.
    private static List<string> ParentsOf(IEntityType child) =>
        [.. child.GetForeignKeys()
            .Where(key =>
                key.PrincipalToDependent is { IsCollection: true } &&
                !ReferenceEquals(key.PrincipalEntityType, child) &&
                typeof(IEntity).IsAssignableFrom(key.PrincipalEntityType.ClrType))
            .Select(static key => key.PrincipalEntityType.ClrType.Name)
            .Distinct(StringComparer.Ordinal)];

    private static string Verb(EntityState state) => state switch
    {
        EntityState.Added => "added",
        EntityState.Deleted => "deleted",
        _ => "changed",
    };
}
