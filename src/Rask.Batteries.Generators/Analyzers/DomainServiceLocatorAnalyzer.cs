using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK141 — an aggregate, an entity or a value object reaches for a service with <c>Current.Service&lt;T&gt;()</c>.
/// </summary>
/// <remarks>
///     The domain decides from what it holds and what it is handed: a service it looks up for itself is a dependency
///     no caller can see, a test has to stand up a whole scope for, and that throws outside one. Hand the value in as
///     a parameter, or raise a domain event and let a handler do the service's part. A suggestion by default; a
///     warning under <c>&lt;RaskStrict&gt;</c>. No quick-fix: which of the two it should be is the author's call.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DomainServiceLocatorAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask141 = new(
        "RASK141",
        "Domain code looks up a service",
        "'{0}' reaches for '{1}' with Current.Service — hand what it needs in as a parameter, or raise a domain event and let a handler use the service",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "An aggregate, an entity or a value object decides from its own state and what its caller hands "
                     + "it. Current.Service<T>() inside one hides a dependency from every caller and test, and throws "
                     + "outside a scope. A suggestion by default; a warning under <RaskStrict>.",
        helpLinkUri: DiagnosticHelp.Link("RASK141"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask141);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName("Rask.Current") is not { } current)
            {
                return;
            }

            start.RegisterOperationAction(context => Analyze(context, current), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol current)
    {
        var call = (IInvocationOperation)context.Operation;
        if (call.TargetMethod is { Name: "Service", IsGenericMethod: true } method &&
            SymbolEqualityComparer.Default.Equals(method.ContainingType, current) &&
            DomainCode.TypeOf(context.ContainingSymbol) is { } domain)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rask141, call.Syntax.GetLocation(), domain.Name,
                method.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }
}
