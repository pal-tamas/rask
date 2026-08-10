using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK043 — a component is being built from code that is not a component, with the factory not
///     imported.
///     <para>
///         The builder surface is reachable only from <i>inside</i> a component: its entries are
///         <b>inherited members</b>, which is the whole point (a static-imported property loses to a
///         same-named type — CS0119 — while a member of the enclosing type wins). Everything else — a
///         test class, a static markup helper, a fixture — reaches components through the generated
///         <c>Generated.Foo(…)</c> factory, which is a <i>method</i> and so beats the same-named type
///         under C#'s invocable-member rule. That is why the factory works in these positions and an
///         entry cannot.
///     </para>
///     <para>
///         When the <c>using static …Generated;</c> is missing, the simple name binds to the component
///         <b>type</b> instead, and the compiler says <b>CS0119</b> ("'Div' is a type, which is not valid
///         in the given context") — sometimes followed by CS0021 on the <c>[…]</c> that would have
///         carried the children. Nothing in that names Rask, the factory, or the one line that fixes it.
///         This does.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FactoryNotImportedAnalyzer : DiagnosticAnalyzer
{
    private const string FactoryClassName = "Generated";

    private static readonly DiagnosticDescriptor Rask043 = new(
        "RASK043",
        "Component factory is not imported here",
        "'{0}' names the component TYPE here, not a call, so this does not compile (CS0119). '{0}' is only a builder entry inside a component or a 'RaskMarkup' host — entries are inherited members — and '{1}' is neither. Derive '{1}' from 'Rask.Core.RaskMarkup' to reach the entry, or add 'using static {2};' to reach the factory.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "The builder surface is reachable only from inside a type that HAS the entries: entries "
                     + "are inherited members, because a static-imported property loses to a same-named type in "
                     + "scope (CS0119) while a member of the enclosing type wins. A component is one such type; "
                     + "so is anything deriving from 'Rask.Core.RaskMarkup', which is Component's own base and "
                     + "carries the framework entries and nothing else — that is the answer for a test class, a "
                     + "fixture or a factory of demo components. Code that is neither reaches components through "
                     + "the generated factory instead. A factory is a METHOD, so C#'s invocable-member rule lets "
                     + "it share its component's name; that is why it works in these positions where an entry "
                     + "cannot. Without the import, the name binds to the type and the compiler reports CS0119, "
                     + "CS0120 or CS0021 — none of which mentions the missing 'using static'.",
        helpLinkUri: DiagnosticHelp.Link("RASK043"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask043);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var component = start.Compilation.GetTypeByMetadataName(BuilderEntry.ComponentFullName);
            if (component is null)
            {
                return;
            }

            // Both shapes a factory call takes: `Div(Class: "x")` and the children indexer that follows
            // it, `Div[…]`. Each binds the same failing simple name, so either one on its own is enough
            // to report — and reporting from both is what covers a call with no arguments.
            start.RegisterSyntaxNodeAction(
                ctx => Analyze(ctx, component, ((InvocationExpressionSyntax)ctx.Node).Expression),
                SyntaxKind.InvocationExpression);
            start.RegisterSyntaxNodeAction(
                ctx => Analyze(ctx, component, ((ElementAccessExpressionSyntax)ctx.Node).Expression),
                SyntaxKind.ElementAccessExpression);
        });
    }

    private static void Analyze(
        SyntaxNodeAnalysisContext context, INamedTypeSymbol component, ExpressionSyntax target)
    {
        // A qualified call (`Generated.Div(…)`, `Parts.Loading()`) already says where it comes from; only
        // a bare simple name can be the one that lost to its own type.
        if (target is not SimpleNameSyntax name)
        {
            return;
        }

        if (Bound(context, name) is not { } type
            || !BuilderEntry.DerivesFromComponent(type, component)
            || type.ContainingNamespace is not { IsGlobalNamespace: false } ns)
        {
            return;
        }

        // Inside a component — or any other RaskMarkup host — this cannot happen: the entry is a member
        // of the enclosing type and wins the lookup outright, and if it somehow did, the answer there is
        // the chain, not an import.
        var enclosing = context.ContainingSymbol?.ContainingType;
        if (enclosing is null || BuilderEntry.IsEntryHost(enclosing, component))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask043,
            name.GetLocation(),
            type.Name,
            enclosing.ToDisplayString(),
            ns.ToDisplayString() + "." + FactoryClassName));
    }

    // The component type the name resolved to, or null. The expression does not compile, so the symbol
    // may arrive as the bound one (Roslyn binds `Div(…)` to the type and reports CS0119) or as a
    // candidate — take either, and only when it really is a named type rather than a method group.
    private static INamedTypeSymbol? Bound(SyntaxNodeAnalysisContext context, SimpleNameSyntax name)
    {
        var info = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken);
        if (info.Symbol is INamedTypeSymbol bound)
        {
            return bound;
        }

        foreach (var candidate in info.CandidateSymbols)
        {
            if (candidate is INamedTypeSymbol named)
            {
                return named;
            }
        }

        return null;
    }
}
