using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

// RASK027 — error when a component factory call wires BOTH the sync and async handler of one event
// (e.g. `Button(OnClick: ..., OnClickAsync: ...)`). Each event has a single backing slot; when both
// siblings are supplied the runtime keeps the sync one and silently ignores the async one, which is
// virtually always a mistake (the author expected the async handler to run). Wire exactly one per
// event. Passing the literal `null` for one of them is allowed (a deliberate "set at most one"
// conditional), so the rule only fires when both arguments are non-null expressions.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SyncAsyncHandlerAnalyzer : DiagnosticAnalyzer
{
    private const string ComponentFullName = "Rask.Core.Component";
    private const string RaskCoreAssembly = "Rask.Core";
    private const string GeneratedClassName = "Generated";

    private static readonly DiagnosticDescriptor Rask027 = new(
        "RASK027",
        "Both the sync and async handler are set for one event",
        "'{0}' and '{1}' are both set on this component — wire only one handler per event. When both "
        + "are supplied the async '{1}' is ignored at runtime (the sync '{0}' wins); pass just one, or "
        + "`null` for the sibling you don't use.",
        "Usage",
        DiagnosticSeverity.Error,
        true,
        "Each DOM event has a single handler slot. Supplying both the sync `OnX` and the async "
        + "`OnXAsync` keeps the sync one and silently drops the async one — set exactly one.",
        DiagnosticHelp.Link("RASK027"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask027);

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

            start.RegisterSyntaxNodeAction(ctx => Analyze(ctx, component), SyntaxKind.InvocationExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol component)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Only the generated component factories — a static `Generated.X(...)` returning a Component.
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method
            || !method.IsStatic
            || !string.Equals(method.ContainingType?.Name, GeneratedClassName, StringComparison.Ordinal)
            || !InheritsFrom(method.ReturnType, component))
        {
            return;
        }

        // Collect the named arguments that carry a non-null expression. Event handlers are passed by
        // name in practice (they sort to the tail of the long factory signatures), so named-arg
        // detection covers the real cases without false positives on positional value props.
        Dictionary<string, ArgumentSyntax>? named = null;
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.NameColon is { } nameColon && !arg.Expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                (named ??= new Dictionary<string, ArgumentSyntax>(StringComparer.Ordinal))
                    [nameColon.Name.Identifier.ValueText] = arg;
            }
        }

        if (named is null)
        {
            return;
        }

        // For each async sibling supplied (OnXAsync), flag when its sync base (OnX) is supplied too.
        foreach (var entry in named)
        {
            var asyncName = entry.Key;
            if (asyncName.Length <= "OnAsync".Length
                || !asyncName.StartsWith("On", StringComparison.Ordinal)
                || !asyncName.EndsWith("Async", StringComparison.Ordinal))
            {
                continue;
            }

            var syncName = asyncName.Substring(0, asyncName.Length - "Async".Length);
            if (named.ContainsKey(syncName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask027, entry.Value.GetLocation(), syncName, asyncName));
            }
        }
    }

    private static bool InheritsFrom(ITypeSymbol? type, INamedTypeSymbol component)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, component))
            {
                return true;
            }
        }

        return false;
    }
}
