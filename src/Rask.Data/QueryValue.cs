using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>A value a query reads as a parameter, not as a constant written into its text — so its plan is reused.</summary>
internal static class QueryValue
{
    // Captured from a real expression, then opened to its definition, so the trimmer can follow it.
    private static readonly MethodInfo EfParameter =
        ((MethodCallExpression)((Expression<Func<object?>>)(() => EF.Parameter<object?>(null))).Body).Method.GetGenericMethodDefinition();

    /// <summary><c>EF.Parameter(value)</c>, in <paramref name="type" />.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "EF.Parameter<T> is closed over a mapped member's own type, which the model already roots.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "As above: the type argument is a mapped member's type.")]
    internal static MethodCallExpression Of(object? value, Type type) =>
        Expression.Call(EfParameter.MakeGenericMethod(type), Expression.Constant(value, type));
}
