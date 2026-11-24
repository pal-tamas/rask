using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Rask.Data;

/// <summary>A value a built query compares with, read through a box so EF Core sees a parameter, not a constant.</summary>
/// <remarks>One cached plan for every value, rather than one per value inlined into the SQL.</remarks>
internal static class QueryParameter
{
    private static readonly FieldInfo BoxedValue =
        typeof(StrongBox<object?>).GetField(nameof(StrongBox<object?>.Value))!;

    /// <summary><paramref name="value" /> as a parameter of <paramref name="type" />.</summary>
    /// <param name="value">The value.</param>
    /// <param name="type">The type the query compares it as: the column's property type.</param>
    /// <returns>The parameter.</returns>
    internal static UnaryExpression Of(object? value, Type type) =>
        Expression.Convert(Expression.Field(Expression.Constant(new StrongBox<object?>(value)), BoxedValue), type);
}
