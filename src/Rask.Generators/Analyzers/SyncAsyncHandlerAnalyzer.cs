using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

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
        DiagnosticHelp.Category,
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

            // The chain reaches none of the above: its steps are extension methods on Build<T>, not a
            // static Generated.Button(...), so the factory branch matched no chain and one of the two
            // handlers was dropped in silence — the exact thing this diagnostic exists to prevent.
            start.RegisterOperationAction(ctx => AnalyzeChain(ctx, component), OperationKind.Invocation);
        });
    }

    private static void AnalyzeChain(OperationAnalysisContext context, INamedTypeSymbol component)
    {
        var operation = (IInvocationOperation)context.Operation;

        // Only the OUTERMOST link, so the chain is judged once and as a whole.
        if (operation.Syntax.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax })
        {
            return;
        }

        if (operation.TargetMethod.IsStatic
            && string.Equals(operation.TargetMethod.ContainingType?.Name, GeneratedClassName, StringComparison.Ordinal))
        {
            return; // The factory branch owns this one.
        }

        if (BuiltComponent(operation.Type) is not { } built || !InheritsFrom(built, component))
        {
            return;
        }

        // Every step written on this chain, and where. A step that was handed `null` is the deliberate
        // "set at most one" conditional shape, exactly as a null argument is in the factory branch.
        var steps = new Dictionary<string, IInvocationOperation>(StringComparer.Ordinal);
        for (var step = operation; step is not null; step = NextStep(step))
        {
            var value = step.TargetMethod.IsExtensionMethod && step.Arguments.Length > 1
                ? step.Arguments[1].Value
                : step.Arguments.Length != 0 ? step.Arguments[0].Value : null;

            if (value is not { ConstantValue: { HasValue: true, Value: null } })
            {
                steps[step.TargetMethod.Name] = step;
            }
        }

        foreach (var entry in steps)
        {
            var asyncName = entry.Key;
            if (asyncName.Length <= "OnAsync".Length
                || !asyncName.StartsWith("On", StringComparison.Ordinal)
                || !asyncName.EndsWith("Async", StringComparison.Ordinal))
            {
                continue;
            }

            var syncName = asyncName.Substring(0, asyncName.Length - "Async".Length);
            if (steps.ContainsKey(syncName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask027, entry.Value.Syntax.GetLocation(), syncName, asyncName));
            }
        }
    }

    // The component a Build<T> is building, or null when the type is anything else.
    private static INamedTypeSymbol? BuiltComponent(ITypeSymbol? type) =>
        type is INamedTypeSymbol { IsGenericType: true, Arity: 1 } named
        && string.Equals(named.ConstructedFrom.ToDisplayString(), "Rask.Core.Build<T>", StringComparison.Ordinal)
            ? named.TypeArguments[0] as INamedTypeSymbol
            : null;

    // The next link DOWN the chain: a step's receiver is whatever produced the Build<T> it extends,
    // written either as an extension method (argument 0) or as an instance call.
    private static IInvocationOperation? NextStep(IInvocationOperation operation)
    {
        var receiver = operation.Instance
                       ?? (operation.TargetMethod.IsExtensionMethod && operation.Arguments.Length != 0
                           ? operation.Arguments[0].Value
                           : null);

        while (receiver is IParenthesizedOperation or IConversionOperation { IsImplicit: true })
        {
            receiver = receiver is IParenthesizedOperation p ? p.Operand : ((IConversionOperation)receiver).Operand;
        }

        return receiver as IInvocationOperation;
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
