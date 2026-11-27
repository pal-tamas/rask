using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>What the analyzers look through to reach the value an author wrote.</summary>
internal static class ImplicitConversions
{
    /// <summary>
    ///     <paramref name="operation" /> without the implicit conversions the compiler wrapped it in — every one of them,
    ///     so a value converted twice on its way (to an interface, then to <c>object</c>) is still the value written. An
    ///     explicit cast stays: the author wrote it.
    /// </summary>
    internal static IOperation WithoutImplicitConversion(this IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true, Operand: var operand })
        {
            operation = operand;
        }

        return operation;
    }
}
