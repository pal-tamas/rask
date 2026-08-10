using System;
using System.Collections.Generic;
using System.Text;
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

    private const string RaskMarkupFullName = "Rask.Core.RaskMarkup";

    private const string SkipFactoryFullName = "Rask.Core.SkipFactoryAttribute";

    private const string GlobalPrefix = "global::";

    /// <summary>
    ///     The component type an entry hands back, or <c>null</c> when <paramref name="member" /> is not
    ///     an entry. Entries are emitted <c>protected static</c> onto <c>Rask.Core.RaskMarkup</c> (the
    ///     framework tags) or onto a consuming component's or markup host's own <c>partial</c>
    ///     (everything else), so the declaring type has to be on the markup surface as well — that is
    ///     what keeps the generated factory class, whose methods have the very same
    ///     name-is-its-type shape, out of this.
    /// </summary>
    public static INamedTypeSymbol? EntryTypeOf(ISymbol member, INamedTypeSymbol component)
    {
        if (!IsEntryHost(member.ContainingType, component))
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

    /// <summary>
    ///     Whether <paramref name="type" /> is somewhere entries can live: a component, or a
    ///     <c>RaskMarkup</c> host (which a component also is — <c>Component</c> derives from
    ///     <c>RaskMarkup</c>, and that is where the framework tags are emitted). Matched by name rather
    ///     than by symbol because every caller already resolves <c>Component</c> and nothing else needs
    ///     the second lookup threaded through it.
    /// </summary>
    public static bool IsEntryHost(ITypeSymbol? type, INamedTypeSymbol component)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, component)
                || string.Equals(current.ToDisplayString(), RaskMarkupFullName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
    ///         Nothing an analyzer can <i>read</i> off a <b>metadata</b> symbol tells a RASK001-required
    ///         property apart from an optional one: a member initializer compiles into the constructor and
    ///         leaves no symbol-level trace, and <c>DeclaringSyntaxReferences</c> is empty there. Only the
    ///         language's own <c>required</c> modifier survives. So a referenced component's requiredness
    ///         is not derived here at all — it is read back from the
    ///         <see cref="PublishedRequiredProperties" /> the owning compilation emitted alongside its
    ///         <c>RaskEntries{Assembly}</c> class. Publish, don't re-derive: that compilation already
    ///         computed it and already reported its diagnostics, and a second derivation would silently
    ///         diverge. Pinned by <c>CrossAssemblyRequiredPropertyTests</c>.
    ///     </para>
    ///     <para>
    ///         A property that still has syntax is in THIS compilation, where the initializer is visible
    ///         and authoritative — the generator may not even have run yet — so the syntax check wins for
    ///         those and the published set is consulted only for the rest.
    ///     </para>
    /// </summary>
    public static List<IPropertySymbol> RequiredProperties(
        INamedTypeSymbol type, PublishedRequiredProperties published, CancellationToken cancellationToken)
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

                if (!IsSettableByAChain(prop) || !IsRequired(prop, type, published, cancellationToken))
                {
                    continue;
                }

                result.Add(prop);
            }
        }

        return result;
    }

    /// <summary>
    ///     The key <c>[assembly: RaskRequiredProperties]</c> names a component by: the fully-qualified
    ///     type with no <c>global::</c> prefix, every type-argument list stripped, and a <c>`n</c> arity
    ///     suffix when the type is generic. Computed from the same
    ///     <see cref="SymbolDisplayFormat.FullyQualifiedFormat" /> string on both sides — the generator
    ///     writes it from <c>Candidate.FullyQualifiedName</c>, the analyzer reads it off the symbol — so
    ///     the two cannot drift.
    /// </summary>
    public static string TypeKey(string fullyQualifiedName)
    {
        var name = new StringBuilder(fullyQualifiedName.Length);
        var depth = 0;
        var arity = 0;
        foreach (var ch in fullyQualifiedName)
        {
            switch (ch)
            {
                case '<':
                    if (depth++ == 0)
                    {
                        arity++;
                    }

                    continue;
                case '>':
                    depth--;
                    continue;
                case ',' when depth == 1:
                    arity++;
                    continue;
            }

            if (depth == 0)
            {
                name.Append(ch);
            }
        }

        if (name.Length > GlobalPrefix.Length
            && string.CompareOrdinal(name.ToString(0, GlobalPrefix.Length), GlobalPrefix) == 0)
        {
            name.Remove(0, GlobalPrefix.Length);
        }

        return arity == 0 ? name.ToString() : name.Append('`').Append(arity).ToString();
    }

    /// <inheritdoc cref="TypeKey(string)" />
    public static string TypeKey(INamedTypeSymbol type) =>
        TypeKey(type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

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

    private static bool IsRequired(
        IPropertySymbol prop,
        INamedTypeSymbol component,
        PublishedRequiredProperties published,
        CancellationToken cancellationToken)
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

        // Syntax ⇒ the property is declared in THIS compilation, where the initializer is right there and
        // is the authoritative answer (the generator that publishes the attribute may not have run yet).
        if (prop.DeclaringSyntaxReferences.Length > 0)
        {
            return prop.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken)
                is PropertyDeclarationSyntax { Initializer: null };
        }

        // No syntax ⇒ metadata, where an initializer is invisible. Ask the assembly that compiled the
        // component rather than guessing from a symbol that cannot carry the answer. Keyed on the
        // COMPONENT being built, not on the property's declaring type: the generator publishes each
        // component's whole required set, inherited props included, exactly as BlocksEntry reads it.
        return published.Contains(component, prop.Name);
    }
}
