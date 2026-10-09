using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     Asks the database whether a row would break a unique rule the model declares — <c>IsUnique("…")</c> —
///     without relying on the index being there.
/// </summary>
/// <remarks>
///     <para>
///         For a schema Rask does not own. There, an index the model declares is a statement about a table
///         somebody else created, and the table may simply not have it: the save would then go through and
///         leave two rows where the rule says one. This runs the rule as a query instead, and answers with the
///         same failure the index's violation would have produced.
///     </para>
///     <para>
///         The query is built from the INDEX and runs through the context's own filters, so a tenant-scoped
///         table is asked about the tenant in flight and nobody else's rows. Three things follow the way a
///         database treats a unique index:
///     </para>
///     <list type="bullet">
///         <item>a NULL in any of the index's columns is not checked — NULLs do not collide;</item>
///         <item>an index filter that only says its own columns are NOT NULL counts as no filter, because of the line above;</item>
///         <item>an index with any other filter is skipped: its condition is SQL, and guessing at it would refuse rows the database accepts.</item>
///     </list>
///     <para>
///         It is a check, not a lock: two saves at the same moment can both pass it. Where the index does exist
///         the database still has the last word, and that refusal is reported the same way.
///     </para>
/// </remarks>
internal static class DeclaredUniqueRules
{
    // Both captured from a real expression, as the tenant filter's readers are, then opened to their definition.
    private static readonly MethodInfo ExistsMethod =
        ((MethodCallExpression)((Expression<Func<DbContext, Task<bool>>>)(c => AnyOfAsync<object>(c, null!, default))).Body)
        .Method.GetGenericMethodDefinition();

    private static readonly MethodInfo EfProperty =
        ((MethodCallExpression)((Expression<Func<object, object?>>)(e => EF.Property<object?>(e, ""))).Body)
        .Method.GetGenericMethodDefinition();

    private static readonly FieldInfo BoxedValue =
        typeof(StrongBox<object?>).GetField(nameof(StrongBox<object?>.Value))!;

    private static readonly char[] Quoting = ['[', ']', '"', '`', '(', ')'];
    private static readonly string[] And = [" AND "];

    /// <summary>
    ///     The failures a row of <paramref name="entityType" /> holding <paramref name="valueOf" /> would cause:
    ///     one for each declared unique rule another row already satisfies.
    /// </summary>
    /// <param name="context">The context to ask through — its filters decide which rows count.</param>
    /// <param name="entityType">The entity the row is of.</param>
    /// <param name="valueOf">The row's value for a property of that entity, by name.</param>
    /// <param name="selfKey">The row's own primary key, so it does not collide with itself; null for a new row.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    internal static async Task<IReadOnlyList<FieldFailure>> CheckAsync(
        DbContext context,
        IEntityType entityType,
        Func<string, object?> valueOf,
        IReadOnlyList<object?>? selfKey,
        CancellationToken cancellationToken)
    {
        List<FieldFailure>? failures = null;

        foreach (var index in Rules(entityType))
        {
            if (await TakenAsync(context, entityType, index, valueOf, selfKey, cancellationToken).ConfigureAwait(false))
            {
                (failures ??= []).Add(UniqueViolation.FailureOf(index));
            }
        }

        return failures ?? [];
    }

    /// <summary>
    ///     Whether another row already holds what <paramref name="valueOf" /> gives for <paramref name="rule" />'s
    ///     columns. False without a query when any of them is NULL.
    /// </summary>
    internal static async Task<bool> TakenAsync(
        DbContext context,
        IEntityType entityType,
        IIndex rule,
        Func<string, object?> valueOf,
        IReadOnlyList<object?>? selfKey,
        CancellationToken cancellationToken) =>
        Predicate(entityType, rule, valueOf, selfKey) is { } taken &&
        await ExistsAsync(context, entityType.ClrType, taken, cancellationToken).ConfigureAwait(false);

    /// <summary>The unique rules of <paramref name="entityType" /> that can be asked as a query.</summary>
    internal static IEnumerable<IIndex> Rules(IEntityType entityType) =>
        entityType.GetIndexes().Where(static index =>
            index.IsUnique &&
            index.FindAnnotation(UniqueViolation.Annotation)?.Value is string &&
            OnlyRulesOutItsOwnNulls(index));

    // "[TenantId] IS NOT NULL", "(\"Name\" IS NOT NULL AND \"TenantId\" IS NOT NULL)" — and nothing else.
    private static bool OnlyRulesOutItsOwnNulls(IIndex index)
    {
        if (index.GetFilter() is not { Length: > 0 } filter)
        {
            return true;
        }

        var store = StoreObjectIdentifier.Create(index.DeclaringEntityType, StoreObjectType.Table);
        var columns = index.Properties
            .Select(p => store is { } table ? p.GetColumnName(table) : p.GetColumnName())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var plain = string.Concat(filter.Split(Quoting)).Trim();

        return plain.Split(And, StringSplitOptions.TrimEntries).All(term =>
            term.EndsWith(" IS NOT NULL", StringComparison.OrdinalIgnoreCase) &&
            columns.Contains(term[..^" IS NOT NULL".Length].Trim()));
    }

    // row => row.A == @a && row.B == @b && !(row.Id == @id), or null when a value is NULL and nothing collides.
    private static LambdaExpression? Predicate(
        IEntityType entityType, IIndex index, Func<string, object?> valueOf, IReadOnlyList<object?>? selfKey)
    {
        var row = Expression.Parameter(entityType.ClrType, "row");
        Expression? same = null;

        foreach (var property in index.Properties)
        {
            if (valueOf(property.Name) is not { } value)
            {
                return null;
            }

            if (Same(row, property, value) is not { } equal)
            {
                return null;
            }

            same = same is null ? equal : Expression.AndAlso(same, equal);
        }

        if (same is null)
        {
            return null;
        }

        if (selfKey is not null && entityType.FindPrimaryKey() is { } key && key.Properties.Count == selfKey.Count)
        {
            Expression? self = null;
            for (var i = 0; i < key.Properties.Count; i++)
            {
                if (Same(row, key.Properties[i], selfKey[i]) is not { } equal)
                {
                    return null;
                }

                self = self is null ? equal : Expression.AndAlso(self, equal);
            }

            same = Expression.AndAlso(same, Expression.Not(self!));
        }

        return Expression.Lambda(same, row);
    }

    // Null for a type with no equality operator: the rule then cannot be asked here, and is left to the database.
    private static BinaryExpression? Same(ParameterExpression row, IProperty property, object? value)
    {
        try
        {
            return Expression.Equal(Stored(row, property), Parameter(value, property.ClrType));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "EF.Property<T> is closed over a mapped property's own type, which the model already roots.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "As above: the type argument is a mapped property's type.")]
    private static MethodCallExpression Stored(ParameterExpression row, IProperty property) =>
        Expression.Call(EfProperty.MakeGenericMethod(property.ClrType), row, Expression.Constant(property.Name));

    // Read through a box rather than inlined, so EF Core sees a parameter: one cached plan, not one per value.
    private static UnaryExpression Parameter(object? value, Type type) =>
        Expression.Convert(Expression.Field(Expression.Constant(new StrongBox<object?>(value)), BoxedValue), type);

    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "The type argument is an entity type of the context's model, which EF Core already roots.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "As above: a mapped entity type, always a reference type.")]
    private static Task<bool> ExistsAsync(
        DbContext context, Type clrType, LambdaExpression taken, CancellationToken cancellationToken) =>
        (Task<bool>)ExistsMethod.MakeGenericMethod(clrType).Invoke(null, [context, taken, cancellationToken])!;

    private static Task<bool> AnyOfAsync<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        DbContext context, LambdaExpression taken, CancellationToken cancellationToken)
        where TEntity : class =>
        context.Set<TEntity>().AsNoTracking().AnyAsync((Expression<Func<TEntity, bool>>)taken, cancellationToken);
}
