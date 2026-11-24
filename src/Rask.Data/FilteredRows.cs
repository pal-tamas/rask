using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     The C# an index's <c>Where(…)</c> was declared with, found again by the SQL it was written as — so the unique
///     rule Rask asks as a query before a save asks it over exactly the rows the database's filtered index holds.
/// </summary>
/// <remarks>
///     Kept beside the model rather than in it: a lambda is no value a migration's snapshot can write, and the filter SQL
///     the model carries already names the declaration, one per entity type and provider.
/// </remarks>
internal static class FilteredRows
{
    private static readonly ConcurrentDictionary<(Type Entity, string Filter), LambdaExpression> Declared = new();

    /// <summary>Records that <paramref name="filter" /> on <paramref name="entity" /> was written from <paramref name="rows" />.</summary>
    internal static void Remember(Type entity, string filter, LambdaExpression rows) => Declared[(entity, filter)] = rows;

    /// <summary>The C# <paramref name="index" />'s filter was written from, or null for one written as SQL.</summary>
    internal static LambdaExpression? Of(IReadOnlyIndex index) =>
        index.GetFilter() is { Length: > 0 } filter ? Declared.GetValueOrDefault((index.DeclaringEntityType.ClrType, filter)) : null;

    /// <summary>The filter as a condition on <paramref name="row" />, for the query that asks the rule.</summary>
    internal static Expression On(LambdaExpression rows, ParameterExpression row) =>
        new Rebinding(rows.Parameters[0], row).Visit(rows.Body);

    /// <summary>
    ///     Whether the row <paramref name="valueOf" /> describes is one the index holds; true when a value it asks is not
    ///     a plain property the row can give (a value object's member), since the database is then the one to say.
    /// </summary>
    internal static bool Keeps(LambdaExpression rows, Func<string, object?> valueOf)
    {
        var known = true;
        var body = new Reading(rows.Parameters[0], valueOf, () => known = false).Visit(rows.Body);
        return !known || Expression.Lambda<Func<bool>>(body).Compile(preferInterpretation: true)();
    }

    // The declared lambda's parameter, as the query's row.
    private sealed class Rebinding(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }

    // Each of the row's members, as the value the row holds.
    private sealed class Reading(ParameterExpression row, Func<string, object?> valueOf, Action unknown) : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression == row)
            {
                return Expression.Constant(valueOf(node.Member.Name), node.Type);
            }

            if (node.Expression is MemberExpression { Expression: var owner } && owner == row)
            {
                unknown();
                return Expression.Default(node.Type);
            }

            return base.VisitMember(node);
        }
    }
}
