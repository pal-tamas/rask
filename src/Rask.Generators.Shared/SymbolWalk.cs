using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

internal static class SymbolWalk
{
    /// <summary>
    /// Every named type under <paramref name="root"/>, nested types included at any depth. One hand-unrolled
    /// level of nesting (#949) found a type inside a container but not one inside a container inside a
    /// container — and the miss is silent: the type simply gets no generated part.
    /// </summary>
    public static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceOrTypeSymbol root)
    {
        foreach (var member in root.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol ns:
                    foreach (var nested in AllTypes(ns))
                    {
                        yield return nested;
                    }

                    break;

                case INamedTypeSymbol type:
                    yield return type;
                    foreach (var nested in AllTypes(type))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }
}
