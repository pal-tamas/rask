using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

// RASK030 — suggest named arguments on a Rask factory call once it passes three or more LEADING
// positional arguments. Beyond one or two, positional calls get hard to read AND fragile: Rask orders
// generated factory parameters by inheritance depth, then by file ordinal + span (see the
// ComponentFactoryGenerator), so a later, unrelated edit — adding a property to a base class, renaming
// a partial file — can reorder parameters and silently rebind a positional call (a `string Id` /
// `string Class` swap compiles clean and misrenders). Naming the arguments (`Prop: value`) makes the
// binding explicit and refactor-proof. The first one or two positional arguments (the primary content —
// `A(href)`, `Div(id, class)`) are left alone as idiomatic. Hidden by default: no build noise, surfaced
// as an IDE suggestion. Suppressible like any analyzer, or globally via `.editorconfig`.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PreferNamedArgsAnalyzer : DiagnosticAnalyzer
{
    private const string ComponentFullName = "Rask.Core.Component";
    private const string RaskCoreAssembly = "Rask.Core";
    private const string GeneratedClassName = "Generated";

    // Three or more positional arguments trips the hint — one or two (the primary content plus, say, a
    // class) stay idiomatic. Deliberately a readability threshold, not a per-type swap analysis: the
    // fix is the same regardless, and a low-noise line keeps the Hidden hint useful.
    private const int PositionalThreshold = 3;

    private static readonly DiagnosticDescriptor Rask030 = new(
        "RASK030",
        "Prefer named arguments on Rask factories",
        "'{0}' is called with {1} positional arguments — name them ('Prop: value') so the call is readable "
        + "and doesn't rebind silently if the generated parameter order shifts (a base-class property "
        + "added, a partial file renamed)",
        "Usage",
        DiagnosticSeverity.Hidden,
        true,
        "Rask orders factory parameters by inheritance depth then file ordinal + span, so a call with "
        + "several positional arguments both reads poorly and can silently rebind when that order changes. "
        + "Naming the arguments makes the binding explicit and refactor-proof.",
        DiagnosticHelp.Link("RASK030"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask030);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (string.Equals(start.Compilation.AssemblyName, RaskCoreAssembly, StringComparison.Ordinal))
            {
                return;
            }

            var component = start.Compilation.GetTypeByMetadataName(ComponentFullName);
            if (component is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(
                ctx => Analyze(ctx, component),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol component)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var model = context.SemanticModel;

        // A Rask factory call: a static method on a class named `Generated` returning a Component subtype.
        if (model.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method
            || !method.IsStatic
            || !string.Equals(method.ContainingType?.Name, GeneratedClassName, StringComparison.Ordinal)
            || !InheritsFrom(method.ReturnType as INamedTypeSymbol, component))
        {
            return;
        }

        // Count the leading positional arguments (C# requires them before any named argument). The
        // children indexer (`[...]`) is a separate ElementAccess, not part of the argument list, so it
        // isn't counted. Three or more trips the hint.
        var args = invocation.ArgumentList.Arguments;
        var positional = 0;
        while (positional < args.Count && args[positional].NameColon is null)
        {
            positional++;
        }

        if (positional < PositionalThreshold)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rask030, NameLocation(invocation), method.Name, positional));
    }

    private static bool InheritsFrom(INamedTypeSymbol? type, INamedTypeSymbol target)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, target))
            {
                return true;
            }
        }

        return false;
    }

    private static Location NameLocation(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.GetLocation(),
        _ => inv.Expression.GetLocation()
    };
}
