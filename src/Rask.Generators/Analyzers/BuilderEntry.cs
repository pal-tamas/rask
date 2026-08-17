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

    private const string RaskMarkupAttributeFullName = "Rask.Core.RaskMarkupAttribute";

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

        // An entry hands back the CHAIN over its component, so unwrap before asking whether the member is
        // named after what it produces — `Build` is never the name of anything an author wrote.
        return ChainedComponent(produced) is INamedTypeSymbol named
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
        // The attribute form, for a type that cannot spend its base slot: when the generator could not
        // give it `RaskMarkup` as a base it injected the framework entries as members instead, so nothing
        // in the base chain says this is a host. The attribute is the only trace, and it is a direct one —
        // read off this type, never inherited, exactly as the generator reads it.
        if (type is INamedTypeSymbol named && HasMarkupAttribute(named))
        {
            return true;
        }

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

    private static bool HasMarkupAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(), RaskMarkupAttributeFullName,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     The component a chain is building, for a <c>Rask.Core.Build&lt;T&gt;</c>; otherwise the type
    ///     unchanged.
    /// </summary>
    /// <remarks>
    ///     Every analyzer here asks "is this expression a component?" of something that is now usually a
    ///     CHAIN over one — the entry, and every step after it, hand back <c>Build&lt;T&gt;</c>. Unwrapping
    ///     in one place is what keeps RASK025/038/044 answering about the component rather than about the
    ///     struct carrying it; each of them went quiet when the receiver changed, which is the failure mode
    ///     an analyzer cannot report on its own.
    /// </remarks>
    public static ITypeSymbol? ChainedComponent(ITypeSymbol? type) =>
        type is INamedTypeSymbol { IsGenericType: true, Arity: 1 } named
        && string.Equals(named.ConstructedFrom.ToDisplayString(), "Rask.Core.Build<T>", StringComparison.Ordinal)
            ? named.TypeArguments[0]
            : type;

    /// <summary>
    ///     Reads a chain from its OUTERMOST link down to the entry that opened it: what it builds, and
    ///     which steps it named along the way.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For the analyzers that ask "was this component given X?", because on a chain that question
    ///         cannot be answered from the outermost call. `Li[…]` is a property reference plus a children
    ///         indexer, and `Img.Src("/x")` ends at a SETTER — so an analyzer matching a method named after
    ///         the component finds nothing and simply never fires. That is not a hypothetical: RASK022 and
    ///         RASK023 were both silently dead on the chain until #704, and the two before them
    ///         (RASK025/038/044) went the same way when the receiver changed.
    ///     </para>
    ///     <para>
    ///         Succeeds only for the outermost link, so one chain is read once rather than once per step.
    ///         An entry is a property typed <c>Build&lt;T&gt;</c>, which is exactly what an ordinary method
    ///         returning a component is not — that distinction is what keeps a static markup helper
    ///         (<c>Ui.Badge(x)</c>) from being mistaken for something that could take a key.
    ///     </para>
    /// </remarks>
    public static bool TryReadChain(
        ExpressionSyntax node,
        SemanticModel model,
        CancellationToken cancellationToken,
        out SimpleNameSyntax entry,
        out ITypeSymbol? built,
        out HashSet<string> steps)
    {
        entry = null!;
        built = null;
        steps = new HashSet<string>(StringComparer.Ordinal);

        // Something chained onto it means this is not the outermost link: `Img.Src("/x")` inside
        // `Img.Src("/x").Alt("a")`, or `Li` inside `Li.Class("c")`.
        if (node.Parent is ElementAccessExpressionSyntax access && access.Expression == node)
        {
            return false;
        }

        if (node.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax })
        {
            return false;
        }

        var current = (ExpressionSyntax?)node;
        while (current is not null)
        {
            switch (current)
            {
                case ElementAccessExpressionSyntax indexer:
                    current = indexer.Expression;
                    continue;
                case ParenthesizedExpressionSyntax paren:
                    current = paren.Expression;
                    continue;
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member }:
                    steps.Add(member.Name.Identifier.ValueText);
                    current = member.Expression;
                    continue;
            }

            break;
        }

        // `Li` is an IdentifierName; a qualified entry (`RaskEntriesX.Li`) is the Name half of a member
        // access, which is how a type in scope with the same simple name is worked around.
        var name = current switch
        {
            IdentifierNameSyntax id => (SimpleNameSyntax)id,
            MemberAccessExpressionSyntax member => member.Name,
            _ => null,
        };

        if (name is null
            || model.GetSymbolInfo(name, cancellationToken).Symbol is not IPropertySymbol property
            || property.Type is not INamedTypeSymbol { IsGenericType: true, Arity: 1 } built1
            || !string.Equals(
                built1.ConstructedFrom.ToDisplayString(), "Rask.Core.Build<T>", StringComparison.Ordinal))
        {
            return false;
        }

        entry = name;
        built = built1.TypeArguments[0];
        return true;
    }

    public static bool DerivesFromComponent(ITypeSymbol? type, INamedTypeSymbol component)
    {
        for (var current = ChainedComponent(type); current is not null; current = current.BaseType)
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

    /// <summary>Whether <paramref name="chain" /> named <paramref name="prop" />.</summary>
    /// <remarks>
    ///     One spelling, because a setter is named after the property it writes — including a delegate
    ///     property. That was not always so: an extension could not share a delegate prop's name while
    ///     the chain received on the component, so the setter dropped a leading <c>On</c>
    ///     (<c>OnSave</c> → <c>.Save(…)</c>) and this had to accept both. The <c>Build&lt;T&gt;</c>
    ///     receiver removed the collision, and keeping the old rule here would be worse than useless:
    ///     RASK038 both looks for the setter in the chain and NAMES it in its message, so a stale rule
    ///     points the reader at a method that does not exist.
    /// </remarks>
    public static bool NamedBy(IPropertySymbol prop, HashSet<string> chain) => chain.Contains(prop.Name);

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
