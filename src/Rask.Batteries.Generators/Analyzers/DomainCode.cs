using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     What the domain analyzers (RASK140, RASK141) treat as domain code: the members of an aggregate, an entity or a
///     value object declared from a Rask.Data base record — and of any type nested in one.
/// </summary>
/// <remarks>
///     The same symbol test RASK130 keys on (<see cref="AggregateShape" />), so the rules never disagree about where
///     the domain is. A plain record an aggregate holds as a value object carries no marker of its own and is not
///     seen from inside: declaring it from <c>Value</c>, <c>Text</c>, <c>Number</c>, <c>Whole</c>, <c>Day</c> or
///     <c>Range</c> is what brings it in.
/// </remarks>
internal static class DomainCode
{
    /// <summary>The nearest domain type <paramref name="where" /> is written in, or null outside one.</summary>
    public static INamedTypeSymbol? TypeOf(ISymbol? where)
    {
        for (var container = where as INamedTypeSymbol ?? where?.ContainingType; container is not null; container = container.ContainingType)
        {
            if (AggregateShape.IsEntity(container) || AggregateShape.IsValueRecord(container))
            {
                return container;
            }
        }

        return null;
    }
}
