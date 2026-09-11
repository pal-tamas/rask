using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK062 — flags children an island cannot render.
///     <para>
///         A Blazor island delivers markup a hosted Blazor component produced, and nothing else. Children would have
///         to be handed across that border, and no crossing is right for every component: a hosted type may have no
///         fragment parameter, one under a name only it knows, or several. So a Blazor island stays a leaf, and every
///         children indexer on one is reported.
///     </para>
///     <para>
///         A JS island takes children of its OWN runtime — a React island accepts React islands, text, numbers and
///         dates — because those travel inside its props and its framework renders them. Rask markup or another
///         runtime's island cannot: <c>ExternalComponent</c> hides <c>Component</c>'s three indexers with ones that
///         return <c>NotAChildOfThisIsland</c>, and only an island's own typed indexers hand the island back. That
///         makes a wrong child a compile error by construction, but the compiler says so late — where the result fails
///         to convert — or never, for <c>var x = Card[Span["x"]]</c>. So a call that binds a hiding indexer is reported
///         here, at the brackets, naming the island and what it accepts.
///     </para>
///     <para>
///         Assigning <c>Children</c> is reported on both families: it goes around every indexer, and an island never
///         renders it.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IslandChildrenAnalyzer : DiagnosticAnalyzer
{
    // By metadata name: the generator cannot reference either island package.
    private const string BlazorBase = "Rask.Blazor.BlazorComponent`1";
    private const string ExternalBase = "Rask.External.ExternalComponent";
    private const string Refusal = "Rask.External.NotAChildOfThisIsland";
    private const string ComponentType = "Rask.Core.Component";

    // What a message calls each runtime, by the base class that declares it.
    private static readonly (string Base, string Runtime)[] Runtimes =
    [
        ("Rask.External.ReactComponent", "React"),
        ("Rask.External.PreactComponent", "Preact"),
        ("Rask.External.SolidComponent", "Solid"),
        ("Rask.External.VueComponent", "Vue"),
        ("Rask.External.SvelteComponent", "Svelte"),
        ("Rask.External.LitComponent", "Lit"),
        ("Rask.External.AngularComponent", "Angular"),
    ];

    // One descriptor, the explanation carried in the message: RS1019 allows an id once per analyzer.
    private static readonly DiagnosticDescriptor Rask062 = new(
        "RASK062",
        "An island cannot render these children",
        "'{0}' {1}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "An island renders what a foreign renderer owns. A Blazor island takes no Rask children; compose "
                     + "the other way round: 'Div[ H2[\"Revenue\"], Chart.Series(_series) ]'. A JS island takes children "
                     + "of its own runtime only — a React island accepts React islands, text, numbers and dates — "
                     + "because its framework renders them; place Rask markup around it instead of inside it.",
        helpLinkUri: DiagnosticHelp.Link("RASK062"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask062);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var compilation = start.Compilation;
            var islands = new Islands(
                compilation.GetTypeByMetadataName(BlazorBase),
                compilation.GetTypeByMetadataName(ExternalBase),
                compilation.GetTypeByMetadataName(Refusal),
                compilation.GetTypeByMetadataName(ComponentType),
                RuntimeBases(compilation));

            // Neither island package is referenced, so no island can exist here and the check is skipped rather
            // than run against every element access in the compilation.
            if (islands.Blazor is null && islands.External is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(ctx => AnalyzeIndexer(ctx, islands), SyntaxKind.ElementAccessExpression);
            start.RegisterOperationAction(ctx => AnalyzeAssignment(ctx, islands), OperationKind.SimpleAssignment);
        });
    }

    private static void AnalyzeIndexer(SyntaxNodeAnalysisContext context, Islands islands)
    {
        var node = (ElementAccessExpressionSyntax)context.Node;
        var receiver = ModelExtensions.GetTypeInfo(context.SemanticModel, node.Expression, context.CancellationToken).Type;
        if (receiver is null)
        {
            return;
        }

        if (Derives(receiver, islands.Blazor))
        {
            Report(context.ReportDiagnostic, node.ArgumentList.GetLocation(), receiver,
                "is a Blazor island and cannot take Rask children; place the markup around it instead of inside it");
            return;
        }

        if (!Derives(receiver, islands.External))
        {
            return;
        }

        // Only the hiding indexers are a mistake. An island's own typed indexer is how it takes children, and a call
        // that bound nothing at all (a bare `null`, ambiguous by design) already has the compiler's error.
        var bound = ModelExtensions.GetSymbolInfo(context.SemanticModel, node, context.CancellationToken).Symbol;
        if (bound is not IPropertySymbol { IsIndexer: true } indexer
            || islands.Refusal is null
            || !SymbolEqualityComparer.Default.Equals(indexer.Type, islands.Refusal))
        {
            return;
        }

        var runtime = RuntimeOf(receiver, islands);
        Report(context.ReportDiagnostic, node.ArgumentList.GetLocation(), receiver, TakesChildren(receiver, islands)
            ? $"is a {runtime} island; its children are {runtime} islands, text, numbers and dates"
            : $"is a {runtime} island that takes no children; place the markup around it instead of inside it");
    }

    private static void AnalyzeAssignment(OperationAnalysisContext context, Islands islands)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        if (assignment.Target is not IPropertyReferenceOperation { Property.Name: "Children" } target
            || islands.Component is null
            || !SymbolEqualityComparer.Default.Equals(target.Property.ContainingType, islands.Component)
            || target.Instance?.Type is not { } receiver)
        {
            return;
        }

        if (Derives(receiver, islands.Blazor))
        {
            Report(context.ReportDiagnostic, target.Syntax.GetLocation(), receiver,
                "is a Blazor island and cannot take Rask children; place the markup around it instead of inside it");
        }
        else if (Derives(receiver, islands.External))
        {
            Report(context.ReportDiagnostic, target.Syntax.GetLocation(), receiver,
                $"is a {RuntimeOf(receiver, islands)} island and never renders Children; pass its children through "
                + "its indexer instead");
        }
    }

    private static void Report(Action<Diagnostic> report, Location location, ITypeSymbol island, string explanation) =>
        report(Diagnostic.Create(Rask062, location, island.Name, explanation));

    /// <summary>
    ///     Whether an island declares a children indexer of its own below <c>ExternalComponent</c>: the generator writes
    ///     them for every island whose component takes content, and none for one that takes nothing.
    /// </summary>
    private static bool TakesChildren(ITypeSymbol island, Islands islands)
    {
        for (var t = island as INamedTypeSymbol; t is not null && !Is(t, islands.External); t = t.BaseType)
        {
            foreach (var member in t.GetMembers())
            {
                if (member is IPropertySymbol { IsIndexer: true } indexer
                    && !SymbolEqualityComparer.Default.Equals(indexer.Type, islands.Refusal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string RuntimeOf(ITypeSymbol island, Islands islands)
    {
        foreach (var (symbol, runtime) in islands.RuntimeBases)
        {
            if (Derives(island, symbol))
            {
                return runtime;
            }
        }

        return "JavaScript";
    }

    private static ImmutableArray<(INamedTypeSymbol Symbol, string Runtime)> RuntimeBases(Compilation compilation)
    {
        // No LINQ here: `Select` on this compilation would bind to Roslyn's incremental-generator extension rather
        // than Enumerable's, and the resulting inference failure is unreadable.
        var found = ImmutableArray.CreateBuilder<(INamedTypeSymbol, string)>();
        foreach (var (name, runtime) in Runtimes)
        {
            if (compilation.GetTypeByMetadataName(name) is { } symbol)
            {
                found.Add((symbol, runtime));
            }
        }

        return found.ToImmutable();
    }

    private static bool Derives(ITypeSymbol type, INamedTypeSymbol? island)
    {
        // BlazorComponent<T> is generic, so a constructed base has to be compared by its original definition — the
        // closed `BlazorComponent<PriceTag>` is never equal to the unbound symbol.
        for (var t = type as INamedTypeSymbol; island is not null && t is not null; t = t.BaseType)
        {
            if (Is(t, island))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Is(INamedTypeSymbol type, INamedTypeSymbol? island) =>
        island is not null && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, island);

    private sealed class Islands(
        INamedTypeSymbol? blazor,
        INamedTypeSymbol? external,
        INamedTypeSymbol? refusal,
        INamedTypeSymbol? component,
        ImmutableArray<(INamedTypeSymbol Symbol, string Runtime)> runtimeBases)
    {
        public INamedTypeSymbol? Blazor { get; } = blazor;
        public INamedTypeSymbol? External { get; } = external;
        public INamedTypeSymbol? Refusal { get; } = refusal;
        public INamedTypeSymbol? Component { get; } = component;
        public ImmutableArray<(INamedTypeSymbol Symbol, string Runtime)> RuntimeBases { get; } = runtimeBases;
    }
}
