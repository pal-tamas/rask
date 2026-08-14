using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Analyzers;

/// <summary>
///     Every <c>[assembly: RaskRequiredProperties("Ns.Widget", "Title")]</c> reachable from a compilation,
///     indexed by <see cref="BuilderEntry.TypeKey(INamedTypeSymbol)" />.
///     <para>
///         This is the metadata half of RASK038. A RASK001-required property — non-nullable with no member
///         initializer — is <b>permanently invisible</b> once the component is behind an assembly boundary:
///         the initializer compiles into the constructor and leaves no symbol-level trace, and a metadata
///         symbol has no <c>DeclaringSyntaxReferences</c> to inspect. Only the language's <c>required</c>
///         modifier survives. So the factory generator publishes the answer from the compilation that owns
///         the component — which already computed it, to decide whether the component may have a builder
///         entry at all — and this reads it back. Re-deriving it here would be a second copy of that rule,
///         free to diverge without anything failing.
///     </para>
///     <para>
///         Built once per compilation in <c>RegisterCompilationStartAction</c>: the scan walks every
///         referenced assembly's attributes, which is far too much to repeat per operation.
///     </para>
/// </summary>
internal sealed class PublishedRequiredProperties
{
    private const string AttributeFullName = "Rask.Core.RaskRequiredPropertiesAttribute";

    private static readonly PublishedRequiredProperties EmptyInstance =
        new(new Dictionary<string, HashSet<string>>(StringComparer.Ordinal));

    private readonly Dictionary<string, HashSet<string>> _byType;

    private PublishedRequiredProperties(Dictionary<string, HashSet<string>> byType) => _byType = byType;

    public static PublishedRequiredProperties For(Compilation compilation)
    {
        Dictionary<string, HashSet<string>>? byType = null;

        // The compilation's own assembly is scanned too: a generator's output is part of the compilation
        // an analyzer sees, so a component declared here has both its syntax and its published entry. The
        // syntax wins (see BuilderEntry.IsRequired) — this is what covers the reference graph.
        Collect(compilation.Assembly, ref byType);
        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            Collect(reference, ref byType);
        }

        return byType is null ? EmptyInstance : new PublishedRequiredProperties(byType);
    }

    /// <summary>
    ///     Whether <paramref name="component" />'s owning assembly published <paramref name="propertyName" />
    ///     as required. The published set is per COMPONENT and already includes what the component inherits,
    ///     so no base walk happens here — but a base declared in a different assembly publishes its own
    ///     components too, and those are merged by the caller's inheritance walk finding the property.
    /// </summary>
    public bool Contains(INamedTypeSymbol component, string propertyName)
    {
        if (_byType.Count == 0)
        {
            return false;
        }

        for (var current = component; current is not null; current = current.BaseType)
        {
            if (_byType.TryGetValue(BuilderEntry.TypeKey(current), out var names)
                && names.Contains(propertyName))
            {
                return true;
            }
        }

        return false;
    }

    private static void Collect(IAssemblySymbol assembly, ref Dictionary<string, HashSet<string>>? byType)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != AttributeFullName
                || attribute.ConstructorArguments.Length != 2
                || attribute.ConstructorArguments[0].Value is not string key
                || key.Length == 0)
            {
                continue;
            }

            byType ??= new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (!byType.TryGetValue(key, out var names))
            {
                byType[key] = names = new HashSet<string>(StringComparer.Ordinal);
            }

            foreach (var name in attribute.ConstructorArguments[1].Values)
            {
                if (name.Value is string propertyName && propertyName.Length != 0)
                {
                    names.Add(propertyName);
                }
            }
        }
    }
}
