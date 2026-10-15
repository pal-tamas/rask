using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     A child of a <see cref="Tenancy.PerTenant" /> aggregate whose table has no tenant column of its own: it
///     belongs to a tenant through its parent, and its filter asks the parent (#1251).
/// </summary>
/// <remarks>
///     <para>
///         A child that declares no <c>TenantId</c> is one of these; a child that declares one — or maps the
///         column in its <c>Configure</c> — carries it, as before. The filter is
///         <c>unrestricted || EXISTS(parent visible, parent.Id = row.ParentId) || …</c>, one EXISTS for each
///         tenant-scoped parent whose collection holds the child, so a table that is a child of two roots
///         (<c>DriverId</c> or <c>VehicleId</c>) keeps a row of either kind.
///     </para>
///     <para>
///         "Visible" is the parent's own filters: its tenant, and its soft delete when it has one. A row whose
///         parent keys are all NULL belongs to nobody, and only <see cref="Tenant.Across" /> reads it.
///     </para>
/// </remarks>
internal static class TenantParents
{
    /// <summary>The entity-type annotation marking a child partitioned through its parent.</summary>
    internal const string Annotation = "Rask:TenantThroughParent";

    private static readonly MethodInfo SetMethod =
        ((MethodCallExpression)((Expression<Func<DbContext, object>>)(c => c.Set<object>())).Body)
        .Method.GetGenericMethodDefinition();

    private static readonly MethodInfo AnyMethod =
        ((MethodCallExpression)((Expression<Func<IQueryable<object>, bool>>)(q => q.Any(x => x != null))).Body)
        .Method.GetGenericMethodDefinition();

    /// <summary>Whether <paramref name="entityType" /> belongs to its tenant through a parent.</summary>
    internal static bool IsThroughParent(IReadOnlyEntityType entityType) =>
        entityType.FindAnnotation(Annotation)?.Value is true;

    /// <summary>
    ///     The relationships through which <paramref name="child" /> belongs to a tenant: each foreign key to a
    ///     parent <paramref name="partitioned" /> says is tenant-scoped, whose collection holds the child.
    /// </summary>
    internal static List<IReadOnlyForeignKey> KeysOf(IReadOnlyEntityType child, Func<Type, bool> partitioned) =>
        [.. child.GetForeignKeys().Where(key =>
            key.PrincipalToDependent is { IsCollection: true } &&
            !ReferenceEquals(key.PrincipalEntityType, child) &&
            partitioned(key.PrincipalEntityType.ClrType))];

    /// <summary>The same, for a write model's entity type, where the registry says which parent is partitioned.</summary>
    internal static List<IForeignKey> KeysOf(IEntityType child) =>
        [.. KeysOf(child, IsPartitioned).Cast<IForeignKey>()];

    /// <summary>Whether the write model partitions <paramref name="entity" /> by tenant.</summary>
    internal static bool IsPartitioned(Type entity) =>
        typeof(IEntity).IsAssignableFrom(entity) && ConventionRegistry.ScopeFor(entity) == Tenancy.PerTenant;

    /// <summary>
    ///     <c>row =&gt; unrestricted || Set&lt;P1&gt;().Any(p =&gt; p.Id == row.P1Id) || …</c> over
    ///     <paramref name="keys" />, the parents' own filters deciding which of them the tenant in flight sees.
    /// </summary>
    internal static LambdaExpression BuildFilter(Type clrType, IReadOnlyList<IReadOnlyForeignKey> keys, DbContext context)
    {
        var row = Expression.Parameter(clrType, "row");
        Expression owned = Expression.Constant(false);

        for (var i = 0; i < keys.Count; i++)
        {
            var exists = ParentExists(row, keys[i], context);
            owned = i == 0 ? exists : Expression.OrElse(owned, exists);
        }

        return Expression.Lambda(Expression.OrElse(TenantColumn.UnrestrictedArm(context), owned), row);
    }

    // Set<TParent>().Any(parent => parent.Id == row.ParentId): one index seek on the parent's key per row.
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "Set<T> and Any<T> are closed over a mapped entity type, which the model already roots.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "As above: a mapped entity type, always a reference type.")]
    private static MethodCallExpression ParentExists(ParameterExpression row, IReadOnlyForeignKey key, DbContext context)
    {
        var parentType = key.PrincipalEntityType.ClrType;
        var parent = Expression.Parameter(parentType, "parent");
        Expression? same = null;

        for (var i = 0; i < key.Properties.Count; i++)
        {
            var equal = Same(
                StoredProperty.Of(parent, key.PrincipalKey.Properties[i]),
                StoredProperty.Of(row, key.Properties[i]));
            same = same is null ? equal : Expression.AndAlso(same, equal);
        }

        var parents = Expression.Call(Expression.Constant(context), SetMethod.MakeGenericMethod(parentType));
        return Expression.Call(AnyMethod.MakeGenericMethod(parentType), parents, Expression.Lambda(same!, parent));
    }

    // A key is `Guid` where the foreign key pointing at it is `Guid?`: the comparison is made in the nullable one.
    private static BinaryExpression Same(Expression key, Expression foreignKey)
    {
        if (key.Type == foreignKey.Type)
        {
            return Expression.Equal(key, foreignKey);
        }

        return Nullable.GetUnderlyingType(foreignKey.Type) == key.Type
            ? Expression.Equal(Expression.Convert(key, foreignKey.Type), foreignKey)
            : Expression.Equal(key, Expression.Convert(foreignKey, key.Type));
    }
}
