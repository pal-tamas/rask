using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using static Rask.Generators.ComponentSymbols;

namespace Rask.Generators.Analyzers;

// RASK026 — warn when StateHasChanged() is called on the component itself from
// inside a Rask event or binding callback (OnChange, OnClick, OnInput, OnAnySubmit, AfterBind, …). Rask
// re-renders the component that owns the callback automatically after the callback runs — even when a
// child control fires it (the framework re-renders the delegate's owner) and even for two-way bindings
// (a write re-renders the binding's authoring component). So a manual StateHasChanged inside one of these
// callbacks is dead weight that teaches the wrong mental model. The canonical anti-pattern this targets is
// `AfterBind: _ => StateHasChanged()`. Suppressible like any analyzer; fires only for a self-call
// (`StateHasChanged()` / `this.StateHasChanged()`), never for `other.StateHasChanged()`.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RedundantStateHasChangedAnalyzer : DiagnosticAnalyzer
{
    private const string RaskCoreAssembly = "Rask.Core";

    private static readonly DiagnosticDescriptor Rask026 = new(
        "RASK026",
        "Redundant StateHasChanged in a Rask callback",
        "StateHasChanged is redundant here — Rask re-renders this component automatically after its '{0}' "
        + "callback runs; remove the call",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        "Rask wraps event callbacks (OnChange/OnClick/OnInput/OnAnySubmit/…) and binding hooks "
        + "(AfterBind/AfterBindAsync) so the component that owns the callback re-renders after it runs — "
        + "including when a child control raised it, and after a two-way bound write. Calling "
        + "StateHasChanged() inside one is unnecessary (the tell-tale anti-pattern is "
        + "'AfterBind: _ => StateHasChanged()'). Remove it; if derived UI still isn't updating, the binding "
        + "or callback owner is wrong, not the render count.",
        DiagnosticHelp.Link("RASK026"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask026);

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

            start.RegisterOperationAction(ctx => Analyze(ctx, component), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol component)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!string.Equals(method.Name, "StateHasChanged", StringComparison.Ordinal)
            || !InheritsFromOrIs(method.ContainingType, component))
        {
            return;
        }

        // Only a self-call (StateHasChanged() / this.StateHasChanged()) is redundant; re-rendering a
        // *different* component from a callback can be intentional, so leave those alone.
        if (invocation.Instance is not IInstanceReferenceOperation)
        {
            return;
        }

        // Find the nearest enclosing lambda/anonymous method and check it is a callback argument to a
        // generated Rask component factory (OnChange/OnClick/AfterBind/…) — only there does the framework
        // guarantee an automatic re-render of the callback owner. Walking only to the nearest lambda avoids
        // flagging a StateHasChanged nested inside some unrelated inner delegate (e.g. a List.ForEach body).
        for (IOperation? op = invocation.Parent; op is not null; op = op.Parent)
        {
            if (op is not IAnonymousFunctionOperation)
            {
                continue;
            }

            if (CallbackStep.NameOf(op) is { } callback)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rask026, invocation.Syntax.GetLocation(), callback));
            }

            return;
        }
    }
}
