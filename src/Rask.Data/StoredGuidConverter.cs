using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.Data;

/// <summary>
///     Stores a <see cref="Guid" /> in the form <see cref="SqlServerGuidOrder" /> sorts by time on SQL Server — after the
///     converter a property has already, a typed id's or an app's, that ends in one.
/// </summary>
internal sealed class StoredGuidConverter : ValueConverter<Guid, Guid>
{
    public StoredGuidConverter()
        : base(static id => SqlServerGuidOrder.ToStore(id), static stored => SqlServerGuidOrder.FromStore(stored))
    {
    }

    /// <summary>The conversion of a bare <see cref="Guid" />.</summary>
    public static StoredGuidConverter Plain { get; } = new();

    /// <summary>
    ///     <paramref name="first" />, the converter a property already has, then the stored form — or the stored form alone
    ///     for a bare <see cref="Guid" />.
    /// </summary>
    /// <remarks>
    ///     Rask's own converter of a one-value value object stays Rask's own, so its <c>.Value</c> still reads the column
    ///     (<see cref="OneValueConverters.Then" />).
    /// </remarks>
    public static ValueConverter After(ValueConverter? first)
    {
        if (first is null)
        {
            return Plain;
        }

        return OneValueConverters.IsOwn(first) ? OneValueConverters.Then(first, Plain) : first.ComposeWith(Plain);
    }

    /// <summary>
    ///     Whether <paramref name="converter" /> stores in the form already — a read face mirrors its write model's
    ///     converter, and storing twice would scramble the id.
    /// </summary>
    public static bool IsIn(ValueConverter? converter) =>
        converter is not null && new StoreCallFinder().Finds(converter.ConvertToProviderExpression);

    private sealed class StoreCallFinder : ExpressionVisitor
    {
        private bool _found;

        public bool Finds(Expression expression)
        {
            Visit(expression);
            return _found;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            _found |= node.Method.DeclaringType == typeof(SqlServerGuidOrder) &&
                      string.Equals(node.Method.Name, nameof(SqlServerGuidOrder.ToStore), StringComparison.Ordinal);
            return base.VisitMethodCall(node);
        }
    }
}
