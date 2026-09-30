using System;
using Microsoft.CodeAnalysis;

namespace Rask.Generators;

/// <summary>What the generators and analyzers ask of a type: is it a Rask component, and can other assemblies see it.</summary>
internal static class ComponentSymbols
{
    /// <summary>The metadata name of <c>Rask.Core.Component</c>.</summary>
    public const string ComponentFullName = "Rask.Core.Component";

    /// <summary>The metadata name of <c>Rask.External.ExternalComponent</c>, the base of every island.</summary>
    public const string ExternalComponentFullName = "Rask.External.ExternalComponent";

    /// <summary>Whether <paramref name="symbol" /> derives, at any depth, from <c>Component</c>.</summary>
    public static bool InheritsFromComponent(INamedTypeSymbol symbol)
    {
        for (var t = symbol.BaseType; t is not null; t = t.BaseType)
        {
            if (string.Equals(t.OriginalDefinition.ToDisplayString(), ComponentFullName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Whether the type is visible outside its assembly, walking containing types so a public nested type
    ///     inside an internal one reads as internal. Generated code that names the type in a signature has to
    ///     match it: a public member over an internal type is CS0051.
    /// </summary>
    public static bool IsExternallyVisible(INamedTypeSymbol symbol)
    {
        for (var t = symbol; t is not null; t = t.ContainingType)
        {
            if (t.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }
}
