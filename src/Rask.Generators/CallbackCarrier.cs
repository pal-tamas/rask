using Microsoft.CodeAnalysis;

namespace Rask.Generators;

/// <summary>Recognises the <c>Rask.Core.Callback</c> family by shape.</summary>
internal static class CallbackCarrier
{
    /// <summary>
    ///     Whether <paramref name="type" /> is a non-nullable <c>Callback</c>, <c>Callback&lt;T&gt;</c> or
    ///     <c>Callback&lt;T1, T2&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     Such a property is OPTIONAL even with no initializer: its <c>default</c> is an unset slot whose
    ///     <c>Invoke</c> does nothing, so a chain has nothing to insist on. Without this it would read as a
    ///     non-nullable property with no initializer — a required step, and RASK001.
    /// </remarks>
    public static bool IsNonNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsValueType: true, Name: "Callback", Arity: <= 2 } named
        && named.ContainingNamespace?.ToDisplayString() == "Rask.Core";
}
