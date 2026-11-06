using System.Linq.Expressions;

namespace Rask.Data;

/// <summary>
///     The root of a model queryable's expression: a placeholder for the rows of one model query, which
///     <see cref="ModelQueryBinder" /> swaps for that query applied to a real context's set when the query runs.
/// </summary>
/// <remarks>
///     A node of its own rather than a constant holding the queryable. EF Core prints a constant whose value is an
///     <see cref="IQueryable" /> through that queryable's own <c>Expression</c> — which for a root was the same
///     constant again, so building the message for an untranslatable query recursed until the stack overflowed and
///     took the process with it. This node has no children and prints as its name.
/// </remarks>
internal sealed class ModelQueryRootExpression(IModelQueryProvider provider, Type type) : Expression
{
    internal IModelQueryProvider Provider { get; } = provider;

    public override ExpressionType NodeType => ExpressionType.Extension;

    public override Type Type { get; } = type;

    public override bool CanReduce => false;

    public override string ToString() => $"ModelQuery<{Provider.ElementType.Name}>";

    protected override Expression VisitChildren(ExpressionVisitor visitor) => this;
}
