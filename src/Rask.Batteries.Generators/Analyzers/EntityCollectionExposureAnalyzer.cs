using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Shared;

namespace Rask.Data.Generators.Analyzers;

/// <summary>
///     RASK081 — an entity hands out a mutable collection of other entities.
/// </summary>
/// <remarks>
///     <para>
///         A private setter does not protect a <c>List&lt;OrderLine&gt;</c>: the list itself is the state,
///         and <c>order.Lines.Add(line)</c> changes it without the order hearing about it. EF Core maps a
///         read-only navigation through its backing field by convention, so the entity can expose
///         <c>IReadOnlyCollection&lt;OrderLine&gt;</c> and keep the list to itself at no cost to mapping.
///     </para>
///     <para>
///         Only a collection of ENTITIES — the element type derives from <c>Rask.Data.Model</c>. A list of
///         strings or of value objects is not a navigation, and mapping one is configured explicitly anyway.
///     </para>
///     <para>
///         "Mutable" is "implements <c>ICollection&lt;T&gt;</c>", less the collections that implement it
///         only to throw: <c>ReadOnlyCollection&lt;T&gt;</c> and the immutable and frozen families. Arrays
///         are not reported — EF Core cannot map one as a navigation, so no entity that works has one.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EntityCollectionExposureAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask081 = new(
        "RASK081",
        "Entity exposes a mutable collection of entities",
        "'{0}.{1}' exposes a mutable '{2}' of '{3}', so code outside '{0}' can add and remove them — keep the "
        + "collection in a private field, expose it as 'IReadOnlyCollection<{3}>', and change it through the "
        + "methods of '{0}'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A private setter does not stop callers from calling Add or Remove on a List<T> an entity "
                     + "exposes. Keep the collection in a private readonly field and expose it as "
                     + "IReadOnlyCollection<T>, IReadOnlyList<T> or IEnumerable<T>; EF Core maps the navigation "
                     + "through the backing field by convention.",
        helpLinkUri: DiagnosticHelp.Link("RASK081"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask081);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var types = ModelTypes.Resolve(start.Compilation);
            var collection = start.Compilation.GetTypeByMetadataName("System.Collections.Generic.ICollection`1");
            if (types.Model is null || collection is null)
            {
                return;
            }

            start.RegisterSymbolAction(ctx => Analyze(ctx, types, collection), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, ModelTypes types, INamedTypeSymbol collection)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!types.IsEntity(type))
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
                } property
                || MutableElementOf(property.Type, collection) is not { } element
                || !IsEntityOrModel(types, element))
            {
                continue;
            }

            var location = property.DeclaringSyntaxReferences.Length > 0
                           && property.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken)
                               is PropertyDeclarationSyntax declaration
                ? declaration.Type.GetLocation()
                : property.Locations[0];

            context.ReportDiagnostic(Diagnostic.Create(
                Rask081,
                location,
                type.Name,
                property.Name,
                property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                element.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    // The T of the ICollection<T> the type is or implements, or null when it is not a mutable collection.
    private static ITypeSymbol? MutableElementOf(ITypeSymbol type, INamedTypeSymbol collection)
    {
        if (type is not INamedTypeSymbol named || IsReadOnlyByDesign(named))
        {
            return null;
        }

        if (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, collection))
        {
            return named.TypeArguments[0];
        }

        foreach (var implemented in named.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, collection))
            {
                return implemented.TypeArguments[0];
            }
        }

        return null;
    }

    // Implements ICollection<T> and throws from every mutator — reporting one would ask the author to wrap a
    // collection that is already read-only.
    private static bool IsReadOnlyByDesign(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString();
        return ns is "System.Collections.Immutable" or "System.Collections.Frozen"
               || (ns == "System.Collections.ObjectModel"
                   && type.Name is "ReadOnlyCollection" or "ReadOnlyObservableCollection" or "ReadOnlySet");
    }

    private static bool IsEntityOrModel(ModelTypes types, ITypeSymbol element) =>
        types.IsEntity(element)
        || SymbolEqualityComparer.Default.Equals(element.OriginalDefinition, types.Model)
        || SymbolEqualityComparer.Default.Equals(element.OriginalDefinition, types.ModelOfId);
}
