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
    ///     Rask.Data's model conventions keep Rask's own converter of a one-value value object Rask's own on top of this
    ///     (<c>OneValueConverters.ThenStoredGuid</c>); this file is compiled into Rask.SqlServer too, which has none.
    /// </remarks>
    public static ValueConverter After(ValueConverter? first) => first is null ? Plain : first.ComposeWith(Plain);

    /// <summary>
    ///     Whether <paramref name="converter" /> stores in the form already — a read face mirrors its write model's
    ///     converter, and storing twice would scramble the id.
    /// </summary>
    /// <remarks>
    ///     Asked by name: Rask.Data's copy of <see cref="SqlServerGuidOrder" /> and Rask.SqlServer's are two types, and the
    ///     stored form either wrote is the same.
    /// </remarks>
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
            _found |= string.Equals(node.Method.DeclaringType?.FullName, typeof(SqlServerGuidOrder).FullName, StringComparison.Ordinal) &&
                      string.Equals(node.Method.Name, nameof(SqlServerGuidOrder.ToStore), StringComparison.Ordinal);
            return base.VisitMethodCall(node);
        }
    }
}
