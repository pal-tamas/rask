using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>
/// Shared naming rules for source generators that emit <c>typeof(...)</c> references to user types.
/// </summary>
/// <remarks>
/// <para>
/// The load-bearing idea: a registry entry needs <b>two different strings</b>, and they must never be
/// derived from one another.
/// </para>
/// <list type="bullet">
/// <item><description>
/// The <b>key</b> is a runtime <i>metadata</i> name — it has to equal what the serializer computes from
/// <c>Type.FullName</c> at run time. Metadata names are unescaped and use <c>+</c> between nesting levels
/// (which the serializers normalize to <c>.</c>).
/// </description></item>
/// <item><description>
/// The <b>type expression</b> is <i>C# syntax</i> — it has to compile inside the generated file, so it
/// needs a <c>global::</c> prefix and <c>@</c>-escaped keyword identifiers.
/// </description></item>
/// </list>
/// <para>
/// Using one string for both roles is exactly what made a job or event declared in a namespace such as
/// <c>@event</c> register under a name the runtime never produces — a key miss, so the message failed to
/// deserialize and silently dead-lettered.
/// </para>
/// </remarks>
internal static class SymbolRegistration
{
    // The runtime metadata name, matching (type.FullName ?? type.Name).Replace('+', '.'):
    //   * GlobalNamespaceStyle.Omitted    — a type in the global namespace keys as "Ev", like FullName.
    //   * NameAndContainingTypesAndNamespaces — "N.Outer.Ev", matching "N.Outer+Ev" post-normalization.
    //   * MiscellaneousOptions.None      — drops EscapeKeywordIdentifiers (FullName has no '@') and
    //                                      UseSpecialTypes (FullName says "System.Int32", not "int").
    //   * GenericsOptions.None           — belt and braces; IsRegisterable already rejects every generic.
    // Do NOT use the parameterless ToDisplayString() here: it defaults to CSharpErrorMessageFormat,
    // which escapes keyword identifiers and would reintroduce the key mismatch.
    private static readonly SymbolDisplayFormat RuntimeNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.None,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.None);

    /// <summary>The registry key: the type's runtime metadata name.</summary>
    public static string RuntimeName(INamedTypeSymbol symbol) =>
        symbol.ToDisplayString(RuntimeNameFormat);

    /// <summary>
    /// The <c>typeof(...)</c> operand: fully-qualified, escaped C#. <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>
    /// emits its own <c>global::</c> prefix — never concatenate one on, or an escaped identifier breaks.
    /// </summary>
    public static string TypeExpression(INamedTypeSymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>
    /// Returns the reason <paramref name="symbol"/> cannot be named from generated code, or
    /// <see langword="null"/> when it can.
    /// </summary>
    /// <remarks>
    /// This is purely about whether a fully-qualified reference would compile in a separate generated
    /// file. A <i>closed</i> generic such as <c>PagedQuery&lt;Product&gt;</c> is perfectly nameable; what
    /// isn't is an unsubstituted type parameter (as in a type nested inside a generic outer, where
    /// <c>Outer&lt;T&gt;.Inner</c> leaks <c>T</c> into the generated file), a file-local type, or anything
    /// private or protected at any level of its containing chain.
    /// </remarks>
    public static string? DescribeUnnameable(ITypeSymbol symbol)
    {
        switch (symbol)
        {
            case ITypeParameterSymbol:
                return "refers to an unsubstituted type parameter";

            case IArrayTypeSymbol array:
                return DescribeUnnameable(array.ElementType);

            case INamedTypeSymbol named:
                // A file-local type is invisible outside its own file, so the generated registry can't
                // name it — and its FullName carries a synthesized "<file>F0__" segment anyway.
                if (named.IsFileLocal)
                {
                    return "is a file-local type";
                }

                for (INamedTypeSymbol? type = named; type is not null; type = type.ContainingType)
                {
                    // The generated code lands in the same assembly, so internal is fine; protected and
                    // private are not — typeof(...) on them would not compile.
                    if (type.DeclaredAccessibility is not (Accessibility.Public
                        or Accessibility.Internal
                        or Accessibility.ProtectedOrInternal))
                    {
                        return ReferenceEquals(type, named)
                            ? "is not accessible from generated code"
                            : $"is nested inside the inaccessible type '{RuntimeName(type)}'";
                    }

                    foreach (var argument in type.TypeArguments)
                    {
                        if (DescribeUnnameable(argument) is { } reason)
                        {
                            return ReferenceEquals(type, named)
                                ? reason
                                : $"is nested inside the generic type '{RuntimeName(type)}'";
                        }
                    }
                }

                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Returns the reason <paramref name="symbol"/> cannot be put in a name-keyed generated registry, or
    /// <see langword="null"/> when it can.
    /// </summary>
    /// <remarks>
    /// Stricter than <see cref="DescribeUnnameable"/>: on top of being nameable, the type's runtime
    /// <c>Type.FullName</c> has to be reconstructible from the symbol, so <i>every</i> generic is out —
    /// a closed generic's <c>FullName</c> carries assembly-qualified type arguments no key would match.
    /// The chain is walked on <see cref="INamedTypeSymbol.Arity"/> rather than
    /// <see cref="INamedTypeSymbol.IsGenericType"/> only so the reported reason is accurate:
    /// <c>IsGenericType</c> conflates "has type parameters" with "is nested in something that does".
    /// </remarks>
    public static string? DescribeUnregisterable(INamedTypeSymbol symbol)
    {
        if (symbol.IsAbstract)
        {
            return "is abstract";
        }

        if (symbol.IsStatic)
        {
            return "is a static type";
        }

        if (symbol.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            return "is not a class or struct";
        }

        for (INamedTypeSymbol? type = symbol; type is not null; type = type.ContainingType)
        {
            if (type.Arity > 0)
            {
                return ReferenceEquals(type, symbol)
                    ? "is a generic type"
                    : $"is nested inside the generic type '{RuntimeName(type)}'";
            }
        }

        return DescribeUnnameable(symbol);
    }

    /// <summary>True when <paramref name="symbol"/> can be put in a name-keyed generated registry.</summary>
    public static bool IsRegisterable(INamedTypeSymbol symbol) =>
        DescribeUnregisterable(symbol) is null;

    /// <summary>True when <paramref name="symbol"/> directly or indirectly implements <paramref name="markerInterface"/>.</summary>
    public static bool ImplementsMarker(INamedTypeSymbol symbol, string markerInterface)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.ToDisplayString() == markerInterface)
            {
                return true;
            }
        }

        return false;
    }
}
