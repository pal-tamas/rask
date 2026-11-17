using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using static Rask.Generators.ComponentSymbols;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK119 — a <c>StateHasChanged()</c> in <c>OnMount</c> or <c>OnUpdated</c> that no <c>await</c> follows.
///     <para>
///         Both hooks run in the render walk, before the component renders: what a hook with no <c>await</c> sets
///         is drawn by that render, and when the hook does await, Rask renders the component again when it
///         completes. So a call with no <c>await</c> after it asks for a render that is coming anyway. A call with an
///         <c>await</c> still to come (before one, or inside a loop that awaits) is left alone, and so is one in a
///         lambda or a local function, which runs on its own schedule. <c>OnFirstRender</c>/<c>OnRendered</c>
///         are not read: one that finishes without yielding is not rendered again, so a call there can be needed.
///         Rask's counterpart of Blazor's BL0012; RASK026 is the same mistake inside an event callback.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LifecycleStateHasChangedAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask119 = new(
        "RASK119",
        "Redundant StateHasChanged in a lifecycle hook",
        "StateHasChanged is redundant here — Rask renders this component after '{0}' returns, and no await follows "
        + "the call; remove it",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        "OnMount and OnUpdated run before the component renders, and a hook that awaits is rendered again when it "
        + "completes. A StateHasChanged() that no await follows asks for a render that is already coming.",
        DiagnosticHelp.Link("RASK119"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask119);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(ComponentFullName) is { } component)
            {
                start.RegisterOperationAction(ctx => Analyze(ctx, component), OperationKind.Invocation);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol component)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsSelfStateHasChanged(invocation, component)
            || IsInNestedFunction(invocation)
            || context.ContainingSymbol is not IMethodSymbol hook
            || !IsRenderingHook(hook, component)
            || invocation.Syntax.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } declaration
            || ((SyntaxNode?)declaration.Body ?? declaration.ExpressionBody) is not { } body
            || AnAwaitFollows(invocation.Syntax, body))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rask119, invocation.Syntax.GetLocation(), hook.Name));
    }

    // `StateHasChanged()` / `this.StateHasChanged()` on a component: re-rendering ANOTHER component is not
    // something the hook's own render does.
    private static bool IsSelfStateHasChanged(IInvocationOperation invocation, INamedTypeSymbol component) =>
        string.Equals(invocation.TargetMethod.Name, "StateHasChanged", StringComparison.Ordinal)
        && invocation.TargetMethod.Parameters.IsEmpty
        && invocation.Instance is IInstanceReferenceOperation
        && InheritsFromOrIs(invocation.TargetMethod.ContainingType, component);

    private static bool IsInNestedFunction(IOperation operation)
    {
        for (var op = operation.Parent; op is not null; op = op.Parent)
        {
            if (op is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return true;
            }
        }

        return false;
    }

    // An override of Component.OnMount / Component.OnUpdated, at any depth.
    private static bool IsRenderingHook(IMethodSymbol method, INamedTypeSymbol component)
    {
        if (!method.IsOverride || method.Name is not ("OnMount" or "OnUpdated"))
        {
            return false;
        }

        var root = method;
        while (root.OverriddenMethod is { } overridden)
        {
            root = overridden;
        }

        return SymbolEqualityComparer.Default.Equals(root.ContainingType, component);
    }

    // True when an await can run after the call: one written after it, or one anywhere in a loop around it
    // (the next pass reaches it). Awaits inside a lambda or local function belong to that function.
    private static bool AnAwaitFollows(SyntaxNode call, SyntaxNode body)
    {
        var awaits = body.DescendantNodes(static n => !IsNestedFunction(n)).Where(IsAwait).ToList();
        if (awaits.Exists(a => a.SpanStart > call.SpanStart))
        {
            return true;
        }

        return call.Ancestors()
            .TakeWhile(a => a != body)
            .Where(IsLoop)
            .Any(loop => awaits.Exists(a => loop.Span.Contains(a.Span)));
    }

    private static bool IsAwait(SyntaxNode node) => node switch
    {
        AwaitExpressionSyntax => true,
        CommonForEachStatementSyntax f => f.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword),
        UsingStatementSyntax u => u.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword),
        LocalDeclarationStatementSyntax d => d.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword),
        _ => false,
    };

    private static bool IsLoop(SyntaxNode node) =>
        node is WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax;

    private static bool IsNestedFunction(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;
}
