using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK062 — flags Rask children passed to an island.
///     <para>
///         An island delivers markup a foreign renderer produced — a Blazor component, a React tree, a
///         Lit element — and nothing else. Children would have to be handed across that border, and
///         there is no crossing that is right for every component: a hosted Blazor type may have no
///         fragment parameter, one under a name only it knows, or several; a front-end component may
///         place them and then never see them again. Both shapes look composable and quietly stop
///         tracking what they were given, which is worse than not offering them.
///     </para>
///     <para>
///         This has to be a compile error rather than a convention. The children indexer lives on
///         <c>Component</c> and <c>Build&lt;T&gt;</c>, so it is available on every chain and cannot be
///         withheld from one type — without this the children bind, compile, and silently never
///         render.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IslandChildrenAnalyzer : DiagnosticAnalyzer
{
    // Both island families, by metadata name: the generator cannot reference either package.
    private static readonly string[] IslandBases =
    [
        "Rask.Blazor.BlazorComponent`1",
        "Rask.External.ExternalComponent",
    ];

    private static readonly DiagnosticDescriptor Rask062 = new(
        "RASK062",
        "An island takes no children",
        "'{0}' is an island and cannot take Rask children; place the markup around it instead of inside it",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "An island renders markup a foreign renderer owns — a hosted Blazor component, a React tree, a "
                     + "Lit element. Rask children would have to cross that border, and no crossing is right for "
                     + "every component, so the island stays a leaf. Compose the other way round: "
                     + "'Div[ H2[\"Revenue\"], Chart.Series(_series) ]' rather than "
                     + "'Chart.Series(_series)[ H2[\"Revenue\"] ]'.",
        helpLinkUri: DiagnosticHelp.Link("RASK062"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask062);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            // No LINQ here: `Select` on this compilation would bind to Roslyn's incremental-generator
            // extension rather than Enumerable's, and the resulting inference failure is unreadable.
            var found = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
            foreach (var name in IslandBases)
            {
                if (start.Compilation.GetTypeByMetadataName(name) is { } island)
                {
                    found.Add(island);
                }
            }

            // Neither island package is referenced, so no island can exist here and the check is
            // skipped rather than run against every element access in the compilation.
            if (found.Count == 0)
            {
                return;
            }

            var bases = found.ToImmutable();

            start.RegisterSyntaxNodeAction(
                ctx => Analyze(ctx, bases),
                SyntaxKind.ElementAccessExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, ImmutableArray<INamedTypeSymbol> bases)
    {
        var node = (ElementAccessExpressionSyntax)context.Node;

        // The receiver of a chain's children indexer is `Build<T>`, never the component — unwrap it or
        // every spelling the chain teaches slips past. Same seam RASK019 uses.
        var receiverType = BuilderEntry.ChainedComponent(ModelExtensions
            .GetTypeInfo(context.SemanticModel, node.Expression, context.CancellationToken).Type);
        if (receiverType is null || !DerivesFromIsland(receiverType, bases))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask062,
            node.ArgumentList.GetLocation(),
            receiverType.Name));
    }

    private static bool DerivesFromIsland(ITypeSymbol type, ImmutableArray<INamedTypeSymbol> bases)
    {
        // BlazorComponent<T> is generic, so a constructed base has to be compared by its original
        // definition — the closed `BlazorComponent<PriceTag>` is never equal to the unbound symbol.
        for (var t = type as INamedTypeSymbol; t is not null; t = t.BaseType)
        {
            foreach (var island in bases)
            {
                if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, island))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
