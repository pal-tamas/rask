using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     Asks, before a save, whether a row it writes would break a <c>b.NoOverlap(…)</c> rule — so the refusal is the
///     rule's message under its field, on every provider, before the database is asked to write anything.
/// </summary>
/// <remarks>
///     <para>
///         The same question the database's rule asks: a row of the same key whose range overlaps the new one, among the
///         rows the rule holds (its <c>Where(…)</c>; a soft-deleted row is filtered out by the soft delete's own filter).
///         The tenant filter is lifted, as the rule's own columns decide whose rows count. A row this save changes or
///         deletes is asked as the save leaves it, not as the database still holds it — two bookings swapped in one save
///         are no conflict.
///     </para>
///     <para>
///         A check, not a lock: two saves at the same moment can both pass it, and the database's rule then has the last
///         word, answered with the same failure (<see cref="NoOverlapViolation" />).
///     </para>
/// </remarks>
internal static class NoOverlapRulesOfSave
{
    private static readonly MethodInfo AnyMethod =
        ((MethodCallExpression)((Expression<Func<DbContext, Task<bool>>>)(c => AnyOfAsync<object>(c, null!, default))).Body)
        .Method.GetGenericMethodDefinition();

    /// <summary>The rows the save adds or changes that a rule holds, with the rules each is asked about.</summary>
    internal static List<(EntityEntry Row, List<NoOverlapRule> Rules)> Touched(DbContext context)
    {
        var touched = new List<(EntityEntry, List<NoOverlapRule>)>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified &&
                entry.Metadata.GetAllBaseTypesInclusive().SelectMany(NoOverlapRule.Of).ToList() is { Count: > 0 } rules)
            {
                touched.Add((entry, rules));
            }
        }

        return touched;
    }

    /// <summary>The failures the rows would cause: one per row and rule another row already overlaps.</summary>
    /// <param name="context">The context to ask through.</param>
    /// <param name="rows">What <see cref="Touched" /> found.</param>
    /// <param name="valueOf">A row's value for a property of its own, as the save writes it.</param>
    /// <param name="placeOf">Where the row sits on its aggregate's form: <c>""</c> for a root.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    internal static async Task<List<FieldFailure>> FailuresAsync(
        DbContext context,
        List<(EntityEntry Row, List<NoOverlapRule> Rules)> rows,
        Func<EntityEntry, string, object?> valueOf,
        Func<EntityEntry, string> placeOf,
        CancellationToken cancellationToken)
    {
        var failures = new List<FieldFailure>();
        foreach (var (row, rules) in rows)
        {
            foreach (var rule in rules)
            {
                if (Held(row, rule, valueOf) is not { } held)
                {
                    continue;
                }

                if (rows.Exists(other => !ReferenceEquals(other.Row, row) && other.Rules.Contains(rule) && Held(other.Row, rule, valueOf) is { } theirs && held.Overlaps(theirs)) ||
                    await TakenAsync(context, row, rule, held, cancellationToken).ConfigureAwait(false))
                {
                    failures.Add(NoOverlapViolation.FailureOf(rule, [placeOf(row)]));
                }
            }
        }

        return failures;
    }

    // What the row holds for the rule, or null when the rule does not hold it: a key part that is null, a row its
    // Where(…) leaves out, a soft-deleted row, or an empty range.
    private static Span? Held(EntityEntry row, NoOverlapRule rule, Func<EntityEntry, string, object?> valueOf)
    {
        var keys = rule.KeyPaths.Select(path => Value(row, path, valueOf)).ToArray();
        var from = Value(row, rule.FromPath, valueOf);
        var to = Value(row, rule.ToPath, valueOf);
        if (keys.Any(static k => k is null) || from is null ||
            (ConventionRegistry.Has(row.Metadata.ClrType, TraitSet.SoftDelete) && row.Metadata.FindProperty(Columns.DeletedAt) is not null &&
             valueOf(row, Columns.DeletedAt) is not null) ||
            (NoOverlapRows.Of(row.Metadata.ClrType, rule.Name) is { } kept && !FilteredRows.Keeps(kept, name => valueOf(row, name))))
        {
            return null;
        }

        var span = new Span(keys, (IComparable)from, to as IComparable);
        return span.Empty ? null : span;
    }

    private static object? Value(EntityEntry row, string path, Func<EntityEntry, string, object?> valueOf)
    {
        var dot = path.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return valueOf(row, path);
        }

        var complex = row.ComplexProperty(path[..dot]);
        return complex.CurrentValue is null ? null : complex.Property(path[(dot + 1)..]).CurrentValue;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "The type argument is an entity type of the context's model, which EF Core already roots.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "As above: a mapped entity type, always a reference type.")]
    private static Task<bool> TakenAsync(DbContext context, EntityEntry row, NoOverlapRule rule, Span held, CancellationToken cancellationToken)
    {
        var type = row.Metadata;
        var x = Expression.Parameter(type.ClrType, "x");
        Expression taken = Expression.Constant(true);
        for (var i = 0; i < rule.KeyPaths.Count; i++)
        {
            var member = Member(x, type, rule.KeyPaths[i]);
            taken = Expression.AndAlso(taken, Expression.Equal(member, QueryParameter.Of(held.Keys[i], member.Type)));
        }

        var from = Member(x, type, rule.FromPath);
        var to = Member(x, type, rule.ToPath);
        Expression open = Nullable(to.Type) == to.Type ? Expression.Equal(to, Expression.Constant(null, to.Type)) : Expression.Constant(false);

        // Not empty, and not after the new row's end nor ending before its start.
        taken = Expression.AndAlso(taken, Expression.OrElse(open, Less(from, to)));
        if (held.To is not null)
        {
            taken = Expression.AndAlso(taken, Less(from, QueryParameter.Of(held.To, to.Type)));
        }

        taken = Expression.AndAlso(taken, Expression.OrElse(open, Less(QueryParameter.Of(held.From, from.Type), to)));

        if (NoOverlapRows.Of(type.ClrType, rule.Name) is { } kept)
        {
            taken = Expression.AndAlso(taken, FilteredRows.On(kept, x));
        }

        // The row itself, and every row this save changes or deletes, is asked as the save leaves it — not here.
        foreach (var other in context.ChangeTracker.Entries()
                     .Where(e => e.State is EntityState.Modified or EntityState.Deleted && type.IsAssignableFrom(e.Metadata)))
        {
            taken = Expression.AndAlso(taken, Expression.Not(Self(x, type, other)));
        }

        return (Task<bool>)AnyMethod.MakeGenericMethod(type.ClrType).Invoke(null, [context, Expression.Lambda(taken, x), cancellationToken])!;
    }

    private static Expression Self(ParameterExpression x, IEntityType type, EntityEntry row)
    {
        Expression same = Expression.Constant(true);
        foreach (var key in type.FindPrimaryKey()!.Properties)
        {
            same = Expression.AndAlso(same, Expression.Equal(
                StoredProperty.Of(x, key), QueryParameter.Of(row.Property(key.Name).OriginalValue, key.ClrType)));
        }

        return same;
    }

    // A property of the row (EF.Property, so a private one too), or a member of one of its value objects.
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "A complex property's CLR type, whose properties EF Core already keeps to materialise it.")]
    private static Expression Member(ParameterExpression x, IEntityType type, string path)
    {
        var dot = path.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return StoredProperty.Of(x, type.FindProperty(path)!);
        }

        var complex = type.FindComplexProperty(path[..dot])!;
        var holder = Expression.Property(x, complex.PropertyInfo!);
        return Expression.Property(holder, complex.ClrType.GetProperty(path[(dot + 1)..])!);
    }

    // a < b, lifted to the nullable type either side has.
    private static BinaryExpression Less(Expression a, Expression b)
    {
        var type = Nullable(a.Type);
        return Expression.LessThan(Lift(a, type), Lift(b, type), liftToNull: false, method: null);
    }

    private static Expression Lift(Expression e, Type type) => e.Type == type ? e : Expression.Convert(e, type);

    private static Type Nullable(Type type) =>
        type.IsValueType && System.Nullable.GetUnderlyingType(type) is null ? typeof(Nullable<>).MakeGenericType(type) : type;

    private static Task<bool> AnyOfAsync<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        DbContext context, LambdaExpression taken, CancellationToken cancellationToken)
        where TEntity : class =>
        context.Set<TEntity>()
            .AsNoTracking()
            .IgnoreQueryFilters([ModelBuilderExtensions.TenantFilter])
            .AnyAsync((Expression<Func<TEntity, bool>>)taken, cancellationToken);

    /// <summary>What a row holds for a rule: its key, and its range — an end of null is open.</summary>
    private sealed record Span(object?[] Keys, IComparable From, IComparable? To)
    {
        internal bool Empty => To is not null && From.CompareTo(To) >= 0;

        internal bool Overlaps(Span other) =>
            Keys.SequenceEqual(other.Keys) &&
            (To is null || other.From.CompareTo(To) < 0) &&
            (other.To is null || From.CompareTo(other.To) < 0);
    }
}
