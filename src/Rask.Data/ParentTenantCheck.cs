using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     Before a save: a child partitioned through its parent (<see cref="TenantParents" />) is written only under a
///     parent of the tenant in flight — the parent it is moving to, and the one it was loaded under.
/// </summary>
/// <remarks>
///     <para>
///         The parent the context tracks answers from its own <c>TenantId</c>, so saving an aggregate asks the
///         database nothing. A parent it does not track is asked for through the context's filters, once per
///         parent: found means it belongs to the tenant in flight.
///     </para>
///     <para><see cref="Tenant.Across" /> is the deliberate way round, as it is for a read.</para>
/// </remarks>
internal static class ParentTenantCheck
{
    private const int Deepest = 4;

    private static readonly MethodInfo ExistsMethod =
        ((MethodCallExpression)((Expression<Func<DbContext, Task<bool>>>)(c => AnyAsync<object>(c, null!, default))).Body)
        .Method.GetGenericMethodDefinition();

    // Whether a model has any child partitioned through its parent: a save of one that has none asks nothing.
    private static readonly ConditionalWeakTable<IModel, StrongBox<bool>> Models = [];

    private static readonly FieldInfo BoxedValue =
        typeof(StrongBox<object?>).GetField(nameof(StrongBox<object?>.Value))!;

    /// <summary>Refuses the save when a child would be written under another tenant's parent.</summary>
    /// <exception cref="InvalidOperationException">It would, or there is no tenant to write it in.</exception>
    internal static async Task RefuseAsync(DbContext context, CancellationToken cancellationToken)
    {
        // Work with no tenant at all is refused by the auditing interceptor, with the exception that says so.
        if (Tenant.IsAcrossTenants || Tenant.IsUnresolved ||
            !Models.GetValue(context.Model, static m => new(m.GetEntityTypes().Any(TenantParents.IsThroughParent))).Value)
        {
            return;
        }

        var tracked = context.ChangeTracker.Entries().ToList();
        var untracked = new List<(EntityEntry Child, IForeignKey Key, object?[] Values)>();

        foreach (var entry in tracked.Where(static e =>
                     e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                     TenantParents.IsThroughParent(e.Metadata)))
        {
            Check(tracked, entry, entry, untracked, depth: 0);
        }

        var asked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (child, key, values) in untracked)
        {
            if (asked.Add(key.PrincipalEntityType.Name + "\u001f" + string.Join('\u001f', values)) &&
                !await VisibleAsync(context, key, values, cancellationToken).ConfigureAwait(false))
            {
                throw Refusal(child, key.PrincipalEntityType.ClrType.Name, "which the tenant in flight cannot read");
            }
        }
    }

    // Every parent the row names, now and as it was loaded; a tracked one answers here, the rest are collected.
    private static void Check(
        List<EntityEntry> tracked,
        EntityEntry child,
        EntityEntry entry,
        List<(EntityEntry Child, IForeignKey Key, object?[] Values)> untracked,
        int depth)
    {
        foreach (var key in TenantParents.KeysOf(entry.Metadata))
        {
            foreach (var values in ParentsNamed(entry, key))
            {
                if (Tracked(tracked, key, values) is not { } parent)
                {
                    untracked.Add((child, key, values));
                }
                else if (TenantParents.IsThroughParent(parent.Metadata))
                {
                    if (depth < Deepest)
                    {
                        Check(tracked, child, parent, untracked, depth + 1);
                    }
                }
                else
                {
                    RefuseUnlessInFlight(child, parent);
                }
            }
        }
    }

    // The key the row is saved with, and — for a row that was loaded — the one it was loaded with, NULLs skipped.
    private static List<object?[]> ParentsNamed(EntityEntry entry, IForeignKey key)
    {
        var named = new List<object?[]>(2);

        if (entry.State is EntityState.Added or EntityState.Modified &&
            ValuesOf(entry, key, original: false) is { } current)
        {
            named.Add(current);
        }

        if (entry.State is EntityState.Modified or EntityState.Deleted &&
            ValuesOf(entry, key, original: true) is { } loaded &&
            !named.Exists(n => n.SequenceEqual(loaded)))
        {
            named.Add(loaded);
        }

        return named;
    }

    private static object?[]? ValuesOf(EntityEntry entry, IForeignKey key, bool original)
    {
        var values = new object?[key.Properties.Count];

        for (var i = 0; i < values.Length; i++)
        {
            var property = entry.Property(key.Properties[i].Name);
            values[i] = original ? property.OriginalValue : property.CurrentValue;

            if (values[i] is null)
            {
                return null;
            }
        }

        return values;
    }

    private static EntityEntry? Tracked(List<EntityEntry> tracked, IForeignKey key, object?[] values) =>
        tracked.Find(candidate =>
            key.PrincipalEntityType.ClrType.IsInstanceOfType(candidate.Entity) &&
            key.PrincipalKey.Properties.Select((p, i) => Equals(candidate.Property(p.Name).CurrentValue, values[i])).All(static same => same));

    // A tracked parent's tenant is its own column — as it was loaded, or as it is being inserted. A parent being
    // inserted with none yet is stamped with the tenant in flight, so there is nothing to compare.
    private static void RefuseUnlessInFlight(EntityEntry child, EntityEntry parent)
    {
        if (parent.Metadata.FindProperty(Columns.TenantId) is null)
        {
            return;
        }

        var column = parent.Property(Columns.TenantId);
        var rows = parent.State == EntityState.Added ? column.CurrentValue : column.OriginalValue;

        if (rows is null && parent.State == EntityState.Added)
        {
            return;
        }

        var tenant = Tenant.Key ?? throw Refusal(
            child, parent.Metadata.ClrType.Name, $"which belongs to tenant {rows}, with no tenant in flight");
        var inFlight = TenantColumn.ValueFor(tenant, column.Metadata.ClrType, child.Metadata.ClrType.Name);

        if (!Equals(rows, inFlight))
        {
            throw Refusal(
                child, parent.Metadata.ClrType.Name, $"which belongs to tenant {rows}, in tenant {inFlight}");
        }
    }

    private static InvalidOperationException Refusal(EntityEntry child, string parent, string why) =>
        new($"'{child.Metadata.ClrType.Name}' belongs to its tenant through its '{parent}' and is being saved under one " +
            $"{why}. A child is written under a parent of the tenant in flight; save it there, or inside " +
            "Tenant.Across() when crossing tenants is the point.");

    // Through the context's own filters: a parent the tenant in flight cannot read is not one it may write under.
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "The type argument is an entity type of the context's model, which EF Core already roots.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "As above: a mapped entity type, always a reference type.")]
    private static Task<bool> VisibleAsync(DbContext context, IForeignKey key, object?[] values, CancellationToken cancellationToken)
    {
        var parentType = key.PrincipalEntityType.ClrType;
        var parent = Expression.Parameter(parentType, "parent");
        Expression? same = null;

        for (var i = 0; i < values.Length; i++)
        {
            var property = key.PrincipalKey.Properties[i];
            var equal = Expression.Equal(
                StoredProperty.Of(parent, property),
                Expression.Convert(Expression.Field(Expression.Constant(new StrongBox<object?>(values[i])), BoxedValue), property.ClrType));
            same = same is null ? equal : Expression.AndAlso(same, equal);
        }

        return (Task<bool>)ExistsMethod.MakeGenericMethod(parentType)
            .Invoke(null, [context, Expression.Lambda(same!, parent), cancellationToken])!;
    }

    private static Task<bool> AnyAsync<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        DbContext context, LambdaExpression same, CancellationToken cancellationToken)
        where TEntity : class =>
        context.Set<TEntity>().AsNoTracking().AnyAsync((Expression<Func<TEntity, bool>>)same, cancellationToken);
}
