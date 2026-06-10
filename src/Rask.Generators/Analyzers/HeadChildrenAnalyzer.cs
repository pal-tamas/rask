using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK019 — flags <c>Head()[...]</c> child indexing.
///     <para>
///         The framework treats <c>&lt;head&gt;</c> as a managed slot — content goes in
///         via the <c>Component? Head</c> override on any component in the tree, and the
///         scoped-css link / scoped-js script tags are auto-emitted. Passing children to
///         the <c>Head</c> element bypasses that pipeline and produces a head that the
///         framework can't dedupe or splice into. We forbid the pattern at compile time.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HeadChildrenAnalyzer : DiagnosticAnalyzer
{
    private const string HeadFullName = "Rask.Core.Components.Head";
    private const string RaskCoreAssembly = "Rask.Core";

    private static readonly DiagnosticDescriptor Rask019 = new(
        "RASK019",
        "<head> is a framework-managed slot — declare contents via Component.Head",
        "Do not pass children to '<head>'; declare head content by overriding the 'RenderResult Head' property on any component instead. The framework collects, dedupes, and splices contributions automatically.",
        "Usage",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK019"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask019);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            // Rask.Core has internal call sites (e.g. RootErrorBoundary's fallback
            // page) that legitimately fill <head> in-line — those framework-managed
            // surfaces aren't user-facing. Mirrors RASK014's skip pattern.
            if (string.Equals(start.Compilation.AssemblyName, RaskCoreAssembly, StringComparison.Ordinal))
            {
                return;
            }

            var headType = start.Compilation.GetTypeByMetadataName(HeadFullName);
            if (headType is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(
                ctx => Analyze(ctx, headType),
                SyntaxKind.ElementAccessExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol headType)
    {
        var node = (ElementAccessExpressionSyntax)context.Node;
        // `Head()[...]` — the indexer receiver expression is typed `Rask.Core.Components.Head`.
        var receiverType = ModelExtensions
            .GetTypeInfo(context.SemanticModel, node.Expression, context.CancellationToken).Type;
        if (receiverType is null)
        {
            return;
        }

        if (!SymbolEqualityComparer.Default.Equals(receiverType, headType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rask019, node.ArgumentList.GetLocation()));
    }
}
