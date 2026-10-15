using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary><c>EF.Property&lt;T&gt;(row, "Name")</c> for a mapped property, shadow or not, in its own type.</summary>
internal static class StoredProperty
{
    // Captured from a real expression, then opened to its definition, so the trimmer can follow it.
    private static readonly MethodInfo EfProperty =
        ((MethodCallExpression)((Expression<Func<object, object?>>)(e => EF.Property<object?>(e, ""))).Body)
        .Method.GetGenericMethodDefinition();

    /// <summary>The value <paramref name="row" /> holds in <paramref name="property" />, as a query reads it.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "EF.Property<T> is closed over a mapped property's own type, which the model already roots.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "As above: the type argument is a mapped property's type.")]
    internal static MethodCallExpression Of(Expression row, IReadOnlyProperty property) =>
        Expression.Call(EfProperty.MakeGenericMethod(property.ClrType), row, Expression.Constant(property.Name));
}
