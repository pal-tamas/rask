using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Rask.Data;

/// <summary>
///     What every <see cref="ComputedColumnBuilderExtensions.Computed{TEntity, TProperty}" /> declared, kept out of the
///     model (the convention forgets it once it is SQL) for what must know a row's computed value before the database
///     wrote it: a numbering whose group is a computed column numbers an added row at the place it is going to.
/// </summary>
internal static class ComputedValues
{
    private static readonly ConcurrentDictionary<(Type Entity, string Member), LambdaExpression> Declared = new();

    /// <summary>Records <paramref name="value" /> as what <paramref name="member" /> of <paramref name="entity" /> is computed as.</summary>
    internal static void Declare(Type entity, string member, LambdaExpression value) => Declared[(entity, member)] = value;

    /// <summary>
    ///     <paramref name="lambda" /> with each computed member of its row read as what it is computed from, so it says in
    ///     memory what the database will hold; the same lambda when it reads none.
    /// </summary>
    internal static LambdaExpression Inlined(LambdaExpression lambda)
    {
        if (Declared.IsEmpty)
        {
            return lambda;
        }

        var row = lambda.Parameters[0];
        var body = new Inliner(row).Visit(lambda.Body);
        return body == lambda.Body ? lambda : Expression.Lambda(body, row);
    }

    private sealed class Inliner(ParameterExpression row) : ExpressionVisitor
    {
        // A computed column the database refuses to compute from itself, so this stops on a cycle the model build would.
        private readonly HashSet<string> _inlining = new(StringComparer.Ordinal);

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression != row || !Declared.TryGetValue((row.Type, node.Member.Name), out var value) || !_inlining.Add(node.Member.Name))
            {
                return base.VisitMember(node);
            }

            var inlined = Visit(NumberingOrder.Rebind.To(value, row));
            _inlining.Remove(node.Member.Name);
            return inlined.Type == node.Type ? inlined : Expression.Convert(inlined, node.Type);
        }
    }
}
