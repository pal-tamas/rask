using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.Analyzers;

/// <summary>
///     Shared shape rules for the builder surface, used by the analyzers that reason about it
///     (<see cref="BuilderEntryAliasAnalyzer" />, <see cref="RequiredBuilderPropertyAnalyzer" />).
///     <para>
///         A <b>builder entry</b> is the member whose <i>name is its component type</i> — the
///         <c>Div</c> property typed <c>Div</c>, or the method entry a generic / DI-constructed
///         component gets instead. Both analyzers recognise an entry by that shape rather than by
///         looking for a generated file, so they hold for a hand-written entry too and never need to
///         be kept in step with the generator's hint names.
///     </para>
/// </summary>
internal static class BuilderEntry
{
    public const string ComponentFullName = "Rask.Core.Component";

    private const string SkipFactoryFullName = "Rask.Core.SkipFactoryAttribute";

    /// <summary>
    ///     The component type an entry hands back, or <c>null</c> when <paramref name="member" /> is not
    ///     an entry. Entries are emitted <c>protected static</c> onto <c>Rask.Core.Component</c> (the
    ///     framework tags) or onto a consuming component's own <c>partial</c> (everything else), so the
    ///     declaring type has to be a component as well — that is what keeps the generated factory
    ///     class, whose methods have the very same name-is-its-type shape, out of this.
    /// </summary>
    public static INamedTypeSymbol? EntryTypeOf(ISymbol member, INamedTypeSymbol component)
    {
        if (!DerivesFromComponent(member.ContainingType, component))
        {
            return null;
        }

        var produced = member switch
        {
            IPropertySymbol { IsIndexer: false } p => p.Type,
            IMethodSymbol { MethodKind: MethodKind.Ordinary, IsExtensionMethod: false } m => m.ReturnType,
            _ => null,
        };

        return produced is INamedTypeSymbol named
               && string.Equals(member.Name, named.Name, StringComparison.Ordinal)
               && DerivesFromComponent(named, component)
            ? named
            : null;
    }

    public static bool DerivesFromComponent(ITypeSymbol? type, INamedTypeSymbol component)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, component))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     The first entry named <paramref name="name" /> reachable on <paramref name="type" /> or any of
    ///     its bases, or <c>null</c>. Used to answer "would this name lose to an entry here?".
    /// </summary>
    public static ISymbol? FindEntry(INamedTypeSymbol? type, string name, INamedTypeSymbol component)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                if (EntryTypeOf(member, component) is not null)
                {
                    return member;
                }
            }
        }

        return null;
    }

    /// <summary>
    ///     The properties a builder chain <i>must</i> name, mirroring the generated factory's
    ///     required-parameter rule (non-nullable, no member initializer — see RASK001) over the same
    ///     property set the setters are emitted from.
    ///     <para>
    ///         Deliberately conservative for a property that comes from <b>metadata</b> rather than
    ///         source: a member initializer is invisible there, so a prop with one would look required
    ///         and produce a wrong answer. Those count only when they carry the language's own
    ///         <c>required</c> modifier, which metadata does preserve.
    ///     </para>
    /// </summary>
    public static List<IPropertySymbol> RequiredProperties(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var result = new List<IPropertySymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var current = type;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop || !seen.Add(prop.Name))
                {
                    continue;
                }

                if (!IsSettableByAChain(prop) || !IsRequired(prop, cancellationToken))
                {
                    continue;
                }

                result.Add(prop);
            }
        }

        return result;
    }

    /// <summary>
    ///     Whether <paramref name="chain" /> named <paramref name="prop" />. A delegate prop's setter
    ///     drops the <c>On</c> prefix (<c>OnSave</c> → <c>.Save(…)</c>), so both spellings count.
    /// </summary>
    public static bool NamedBy(IPropertySymbol prop, HashSet<string> chain)
    {
        if (chain.Contains(prop.Name))
        {
            return true;
        }

        return prop.Type is INamedTypeSymbol { TypeKind: TypeKind.Delegate }
               && prop.Name.Length > 2
               && prop.Name.StartsWith("On", StringComparison.Ordinal)
               && chain.Contains(prop.Name.Substring(2));
    }

    // The filter the setter emission uses: a public settable instance property that is not the Children
    // slot, not opted out, and not init-only (an init-only prop can only be assigned in an object
    // initializer — CS8852 — so no setter exists for a chain to call).
    private static bool IsSettableByAChain(IPropertySymbol prop)
    {
        if (prop.IsStatic || prop.IsIndexer || prop.IsImplicitlyDeclared || prop.IsOverride)
        {
            return false;
        }

        if (prop.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        if (prop.SetMethod is not { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false })
        {
            return false;
        }

        if (string.Equals(prop.Name, "Children", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var attribute in prop.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == SkipFactoryFullName)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRequired(IPropertySymbol prop, CancellationToken cancellationToken)
    {
        if (prop.IsRequired)
        {
            return true;
        }

        if (prop.Type.NullableAnnotation == NullableAnnotation.Annotated
            || (prop.Type.IsValueType
                && prop.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T))
        {
            return false;
        }

        // No syntax ⇒ the property came from a referenced assembly and its initializer, if any, is not
        // observable. Stay quiet rather than guess.
        return prop.DeclaringSyntaxReferences.Length > 0
               && prop.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken)
                   is PropertyDeclarationSyntax { Initializer: null };
    }
}
