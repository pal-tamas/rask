using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

// RASK126 — warn when a context is configured with UseLazyLoadingProxies but never told AllowLazyLoading(). Rask.Data
// switches lazy loading off on every context its interceptors reach, so the proxies are built and then load nothing: the
// navigation reads null or empty, which looks like missing data. Saying AllowLazyLoading() next to it is the explicit,
// discouraged opt-in. Scoped to the body the call sits in (the options lambda, or the method), as the two calls are
// written together.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LazyLoadingProxiesAnalyzer : DiagnosticAnalyzer
{
    private const string ProxiesMethod = "UseLazyLoadingProxies";
    private const string ProxiesType = "Microsoft.EntityFrameworkCore.ProxiesExtensions";
    private const string OptIn = "AllowLazyLoading";

    private static readonly DiagnosticDescriptor Rask126 = new(
        "RASK126",
        "Lazy-loading proxies on a context that does not allow lazy loading",
        "UseLazyLoadingProxies has no effect here: Rask switches lazy loading off unless the context's options call "
        + ".AllowLazyLoading() — load what a read needs with Include or a projection, or opt in explicitly",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
        "Rask.Data turns ChangeTracker.LazyLoadingEnabled off on every context it configures, because a navigation read "
        + "in a loop, a render or a validation is a query nobody wrote (N+1). Lazy-loading proxies on such a context are "
        + "built and then load nothing, so a navigation reads null or empty. Say what a read needs instead (Include, a "
        + "projection, a read face), or opt in with .AllowLazyLoading() on the same options.",
        helpLinkUri: DiagnosticHelp.Link("RASK126"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask126);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!string.Equals(MethodName(invocation), ProxiesMethod, StringComparison.Ordinal))
        {
            return; // Cheap syntactic filter before the semantic lookup.
        }

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method
            || !string.Equals(
                (method.ReducedFrom ?? method).ContainingType?.ToDisplayString(), ProxiesType, StringComparison.Ordinal))
        {
            return;
        }

        var optedIn = EnclosingScope(invocation).DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(static call => string.Equals(MethodName(call), OptIn, StringComparison.Ordinal));
        if (!optedIn)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask126, NameLocation(invocation)));
        }
    }

    // The body the call sits in: the options lambda, a method, a local function, an accessor, or top-level statements.
    private static SyntaxNode EnclosingScope(SyntaxNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is AnonymousFunctionExpressionSyntax or BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax
                or AccessorDeclarationSyntax or CompilationUnitSyntax)
            {
                return current;
            }
        }

        return node;
    }

    private static string? MethodName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        SimpleNameSyntax name => name.Identifier.ValueText,
        _ => null,
    };

    private static Location NameLocation(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.GetLocation(),
        _ => invocation.GetLocation(),
    };
}
