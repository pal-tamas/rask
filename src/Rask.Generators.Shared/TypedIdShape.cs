using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.Shared;

/// <summary>
///     The one line Rask completes into a typed id: <c>public readonly partial record struct VehicleId(int Value);</c>.
/// </summary>
/// <remarks>
///     <para>
///         The rule is read off the declaration and nothing else, so it is the same for every generator that asks: a
///         top-level, non-generic <c>readonly partial record struct</c> whose name ends in <c>Id</c>, with ONE positional
///         parameter named <c>Value</c> of <c>int</c>, <c>long</c>, <c>Guid</c> or <c>string</c>. Anything else is an
///         ordinary type, so a <c>record struct Count(int Value)</c> keeps the record's own <c>ToString</c>.
///     </para>
///     <para>
///         Every typed id is also what <c>WireShape</c> (Rask.Batteries.Generators) sends as the bare value it holds (a record struct with one
///         public constructor feeding one property), so a command carrying one is <c>42</c> on Rask's wire and in JSON
///         alike. The rule here is narrower on purpose: it decides which types get generated members — a
///         <c>ToString</c> among them — and that needs the author's explicit <c>partial</c> and the <c>Id</c> name.
///     </para>
///     <para>
///         Shared because generators cannot see each other's output: the routes generator has to know a typed id
///         parses before the generated <c>IParsable</c> exists.
///     </para>
/// </remarks>
internal static class TypedIdShape
{
    private const string Suffix = "Id";
    private const string ValueName = "Value";

    /// <summary>Whether <paramref name="node" /> may declare a typed id — the cheap syntax half, before any symbol.</summary>
    public static bool IsCandidate(SyntaxNode node) =>
        node is RecordDeclarationSyntax
        {
            ClassOrStructKeyword.RawKind: (int)SyntaxKind.StructKeyword,
            TypeParameterList: null,
            ParameterList.Parameters.Count: 1,
        } record &&
        record.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax &&
        record.Identifier.ValueText.EndsWith(Suffix, StringComparison.Ordinal) &&
        string.Equals(record.ParameterList.Parameters[0].Identifier.ValueText, ValueName, StringComparison.Ordinal) &&
        record.Modifiers.Any(SyntaxKind.PartialKeyword) &&
        record.Modifiers.Any(SyntaxKind.ReadOnlyKeyword);

    /// <summary>What the typed id <paramref name="type" /> declared in this compilation holds, or <see cref="TypedIdValue.None" />.</summary>
    public static TypedIdValue Of(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol { IsRecord: true, TypeKind: TypeKind.Struct, IsGenericType: false } named ||
            named.ContainingType is not null)
        {
            return TypedIdValue.None;
        }

        var declared = named.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<RecordDeclarationSyntax>()
            .Any(IsCandidate);

        if (!declared)
        {
            return TypedIdValue.None;
        }

        var value = named.InstanceConstructors
            .Where(static ctor => ctor.Parameters.Length == 1 &&
                                  string.Equals(ctor.Parameters[0].Name, ValueName, StringComparison.Ordinal))
            .Select(static ctor => ctor.Parameters[0].Type)
            .FirstOrDefault();

        return ValueOf(value);
    }

    /// <summary>
    ///     What the typed id <paramref name="type" /> compiled into a referenced assembly holds, or
    ///     <see cref="TypedIdValue.None" />.
    /// </summary>
    /// <remarks>
    ///     A referenced type has no declaration to read, so it is known by what its declaration compiled to: a
    ///     <c>readonly</c> struct with ONE public constructor, taking a <c>Value</c> of <c>int</c>, <c>long</c>,
    ///     <c>Guid</c> or <c>string</c>, that is <c>ISpanParsable</c> of itself — the members TypedIdGenerator gave it.
    ///     Not <c>IsRecord</c>: metadata does not say a struct was a record, since what marks one is private.
    /// </remarks>
    public static TypedIdValue OfReferenced(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol { IsReadOnly: true, TypeKind: TypeKind.Struct, IsGenericType: false } named ||
            named.ContainingType is not null ||
            !named.AllInterfaces.Any(face => IsSpanParsableOf(face, named)))
        {
            return TypedIdValue.None;
        }

        var constructors = named.InstanceConstructors
            .Where(static ctor => ctor is { DeclaredAccessibility: Accessibility.Public, IsImplicitlyDeclared: false })
            .ToList();

        return constructors.Count == 1 && constructors[0].Parameters.Length == 1 &&
               string.Equals(constructors[0].Parameters[0].Name, ValueName, StringComparison.Ordinal)
            ? ValueOf(constructors[0].Parameters[0].Type)
            : TypedIdValue.None;
    }

    /// <summary>Whether <paramref name="type" /> is a typed id, declared in this compilation or compiled into a referenced one.</summary>
    public static bool Is(ITypeSymbol? type) => Of(type) != TypedIdValue.None || OfReferenced(type) != TypedIdValue.None;

    private static bool IsSpanParsableOf(INamedTypeSymbol face, INamedTypeSymbol self) =>
        face is { Name: "ISpanParsable", TypeArguments.Length: 1, ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } &&
        SymbolEqualityComparer.Default.Equals(face.TypeArguments[0], self);

    private static TypedIdValue ValueOf(ITypeSymbol? value) => value switch
    {
        { SpecialType: SpecialType.System_Int32 } => TypedIdValue.Int32,
        { SpecialType: SpecialType.System_Int64 } => TypedIdValue.Int64,
        { SpecialType: SpecialType.System_String } => TypedIdValue.String,
        { Name: "Guid", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } =>
            TypedIdValue.Guid,
        _ => TypedIdValue.None,
    };
}
