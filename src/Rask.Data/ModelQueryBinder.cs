using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     Binds every model query an expression reaches to one context: each root becomes that query applied to the
///     context's own set, so a read face used inside another's LINQ — <c>TruckFleet.AsQueryable()</c> in an
///     <c>OrderBy</c> over trucks — is translated by EF Core as a correlated subquery of the same SQL statement.
/// </summary>
/// <remarks>
///     <para>
///         A root met directly (the outer query's, or another composed into it by <c>Concat</c> and friends) is
///         replaced where it stands. One written inside a lambda is not a root yet: it is the call or the captured
///         variable that produces one, which EF Core would evaluate itself. So a parameter-free node of a queryable
///         type inside a lambda is evaluated here, and replaced only when what it produced is a model queryable —
///         anything else is left for EF Core exactly as it was.
///     </para>
///     <para>
///         Each bound query is the model query's own <c>Apply</c> over the set, so its no-tracking head, its composed
///         operators and the context's query filters (soft delete, tenant) all hold for the inner rows as they do
///         for the outer ones.
///     </para>
/// </remarks>
internal sealed class ModelQueryBinder(DbContext context) : ExpressionVisitor
{
    private int _lambdaDepth;

    /// <summary><paramref name="query" /> with every nested model query bound to <paramref name="context" />.</summary>
    internal static IQueryable<T> BindNested<T>(IQueryable<T> query, DbContext context)
    {
        var bound = new ModelQueryBinder(context).Visit(query.Expression);
        return ReferenceEquals(bound, query.Expression) ? query : query.Provider.CreateQuery<T>(bound);
    }

    protected override Expression VisitExtension(Expression node) =>
        node is ModelQueryRootExpression root ? VisitQuery(root.Provider.SourceIn(context)) : base.VisitExtension(node);

    protected override Expression VisitConstant(ConstantExpression node) =>
        node.Value is IQueryable { Provider: IModelQueryProvider } queryable ? VisitQuery(queryable.Expression) : node;

    protected override Expression VisitLambda<T>(Expression<T> node)
    {
        _lambdaDepth++;
        try
        {
            return base.VisitLambda(node);
        }
        finally
        {
            _lambdaDepth--;
        }
    }

    protected override Expression VisitMember(MemberExpression node) =>
        Evaluated(node) ?? base.VisitMember(node);

    protected override Expression VisitMethodCall(MethodCallExpression node) =>
        Evaluated(node) ?? base.VisitMethodCall(node);

    // The bound form of a call or captured variable inside a lambda that produces a model queryable, or null when
    // the node is anything else — which then visits on as usual.
    private Expression? Evaluated(Expression node)
    {
        if (_lambdaDepth == 0 || !typeof(IQueryable).IsAssignableFrom(node.Type) || Unevaluatable.Reaches(node))
        {
            return null;
        }

        return Evaluate(node) is IQueryable { Provider: IModelQueryProvider } queryable
            ? VisitQuery(queryable.Expression)
            : null;
    }

    // A query standing in for a root is a query of its own, not code inside the lambda it was met in: its
    // operators over EF Core's set are what EF translates, so none of them is evaluated here.
    private Expression VisitQuery(Expression query)
    {
        var depth = _lambdaDepth;
        _lambdaDepth = 0;
        try
        {
            return Visit(query);
        }
        finally
        {
            _lambdaDepth = depth;
        }
    }

    private static object? Evaluate(Expression node)
    {
        // A captured variable — the common case — is read straight off its closure, with nothing compiled.
        if (node is MemberExpression { Expression: ConstantExpression closure } member)
        {
            switch (member.Member)
            {
                case FieldInfo field:
                    return field.GetValue(closure.Value);
                case PropertyInfo property:
                    return property.GetValue(closure.Value);
            }
        }

        return Expression.Lambda<Func<object?>>(Expression.Convert(node, typeof(object)))
            .Compile(preferInterpretation: true)();
    }

    // Whether an expression reads a lambda parameter, or holds a node only a query provider understands (EF Core's
    // query root, a model query's) — either way it cannot be evaluated on its own.
    private sealed class Unevaluatable : ExpressionVisitor
    {
        private bool _found;

        internal static bool Reaches(Expression node)
        {
            var finder = new Unevaluatable();
            finder.Visit(node);
            return finder._found;
        }

        public override Expression? Visit(Expression? node) => _found ? node : base.Visit(node);

        protected override Expression VisitParameter(ParameterExpression node)
        {
            _found = true;
            return node;
        }

        protected override Expression VisitExtension(Expression node)
        {
            _found = true;
            return node;
        }
    }
}
