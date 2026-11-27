using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK193 — an aggregate holds a list of its children by hand: a private <c>List&lt;X&gt;</c> behind an
///     <c>IReadOnlyList&lt;X&gt;</c> (or <c>IReadOnlyCollection&lt;X&gt;</c>, <c>IEnumerable&lt;X&gt;</c>) view, or a
///     public <c>List&lt;X&gt;</c> / <c>ICollection&lt;X&gt;</c> — where one <c>HasMany&lt;X&gt;</c> says the same.
/// </summary>
/// <remarks>
///     <para>
///         The pair is two members for one thing, and only one of them is guarded: the aggregate's other methods, and
///         anything that reaches the field, change the list without RASK180 seeing it. <c>HasMany&lt;X&gt;</c> is the
///         list and the view at once, its mutators the aggregate's alone, its <c>Sync</c> the form's rows made children —
///         and EF Core maps it as it mapped the field, so the move writes no migration.
///     </para>
///     <para>
///         Only children: the element is an entity that is not an aggregate. A list of another aggregate's rows is
///         RASK087's. A suggestion everywhere, an error under <c>&lt;RaskStrict&gt;</c>.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HandListedChildrenAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic's property naming the backing field, for the code fix; absent for a public list.</summary>
    public const string FieldProperty = "Field";

    private static readonly DiagnosticDescriptor Rask192 = new(
        "RASK193",
        "A child list exposed by hand",
        "'{0}.{1}' holds its '{2}' children by hand — declare 'public HasMany<{2}> {1} {{ get; }} = new();', which only "
        + "'{0}' changes",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A private List<X> field with a read-only view of it, or a public List<X> or ICollection<X>, is the "
                     + "aggregate's children written by hand. Declare them as one HasMany<X>: everyone reads it, only the "
                     + "aggregate changes it (RASK180), and EF Core maps it as it mapped the list — no migration.",
        helpLinkUri: DiagnosticHelp.Link("RASK193"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask192);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var types = ModelTypes.Resolve(start.Compilation);
            if (types.EntityOfId is null)
            {
                return;
            }

            start.RegisterSymbolAction(ctx => Analyze(ctx, types), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, ModelTypes types)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!types.IsEntity(type) || !AggregateShape.IsAggregate(type))
        {
            return;
        }

        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol
                {
                    IsStatic: false, IsIndexer: false, IsOverride: false,
                    DeclaredAccessibility: Accessibility.Public,
                    GetMethod.DeclaredAccessibility: Accessibility.Public,
                    Type: INamedTypeSymbol { TypeArguments.Length: 1 } held,
                } property
                || property.DeclaringSyntaxReferences.Length == 0
                || property.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken) is not PropertyDeclarationSyntax declaration
                || held.TypeArguments[0] is not INamedTypeSymbol child
                || !types.IsEntity(child) || AggregateShape.IsAggregate(child))
            {
                continue;
            }

            var properties = ImmutableDictionary<string, string?>.Empty;
            if (!IsMutableList(held))
            {
                if (!IsView(held) || BackingList(type, declaration, child) is not { } field)
                {
                    continue;
                }

                properties = properties.Add(FieldProperty, field.Name);
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rask192, declaration.Type.GetLocation(), properties, type.Name, property.Name, child.Name));
        }
    }

    // The private List<X> the view's getter hands out: `=> _parts`, `=> _parts.AsReadOnly()`, `{ get { return _parts; } }`.
    private static IFieldSymbol? BackingList(INamedTypeSymbol type, PropertyDeclarationSyntax declaration, INamedTypeSymbol child)
    {
        if (GetterOf(declaration) is not { } body)
        {
            return null;
        }

        var named = body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Select(static n => n.Identifier.ValueText).ToImmutableHashSet(StringComparer.Ordinal);
        return type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f =>
            f is { IsStatic: false, DeclaredAccessibility: Accessibility.Private, Type: INamedTypeSymbol fieldType } &&
            named.Contains(f.Name) &&
            string.Equals(fieldType.OriginalDefinition.ToDisplayString(), "System.Collections.Generic.List<T>", StringComparison.Ordinal) &&
            SymbolEqualityComparer.Default.Equals(fieldType.TypeArguments[0], child));
    }

    private static SyntaxNode? GetterOf(PropertyDeclarationSyntax declaration)
    {
        if (declaration.ExpressionBody is { } arrow)
        {
            return arrow.Expression;
        }

        var get = declaration.AccessorList?.Accessors.FirstOrDefault(static a => a.Keyword.IsKind(SyntaxKind.GetKeyword));
        return (SyntaxNode?)get?.ExpressionBody?.Expression ?? get?.Body;
    }

    private static bool IsMutableList(INamedTypeSymbol held) =>
        string.Equals(held.OriginalDefinition.ToDisplayString(), "System.Collections.Generic.List<T>", StringComparison.Ordinal) ||
        held.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_ICollection_T;

    private static bool IsView(INamedTypeSymbol held) =>
        held.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IReadOnlyList_T
            or SpecialType.System_Collections_Generic_IReadOnlyCollection_T
            or SpecialType.System_Collections_Generic_IEnumerable_T;
}
