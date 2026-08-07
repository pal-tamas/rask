using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

// RASK026 — warn when StateHasChanged()/StateHasChangedAsync() is called on the component itself from
// inside a Rask event or binding callback (OnChange, OnClick, OnInput, OnSubmit, AfterBind, …). Rask
// re-renders the component that owns the callback automatically after the callback runs — even when a
// child control fires it (the framework re-renders the delegate's owner) and even for two-way bindings
// (a write re-renders the binding's authoring component). So a manual StateHasChanged inside one of these
// callbacks is dead weight that teaches the wrong mental model. The canonical anti-pattern this targets is
// `AfterBind: _ => StateHasChanged()`. Suppressible like any analyzer; fires only for a self-call
// (`StateHasChanged()` / `this.StateHasChanged()`), never for `other.StateHasChanged()`.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RedundantStateHasChangedAnalyzer : DiagnosticAnalyzer
{
    private const string ComponentFullName = "Rask.Core.Component";
    private const string RaskCoreAssembly = "Rask.Core";
    private const string RaskCoreNamespace = "Rask.Core";

    private static readonly DiagnosticDescriptor Rask026 = new(
        "RASK026",
        "Redundant StateHasChanged in a Rask callback",
        "StateHasChanged is redundant here — Rask re-renders this component automatically after its '{0}' "
        + "callback runs; remove the call",
        "Usage",
        DiagnosticSeverity.Warning,
        true,
        "Rask wraps event callbacks (OnChange/OnClick/OnInput/OnSubmit/…) and binding hooks "
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

        if (!(string.Equals(method.Name, "StateHasChanged", StringComparison.Ordinal)
              || string.Equals(method.Name, "StateHasChangedAsync", StringComparison.Ordinal))
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

            if (CallbackNameOf(op) is { } callback)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rask026, invocation.Syntax.GetLocation(), callback));
            }

            return;
        }
    }

    // The Rask callback a lambda is bound to (by name), or null. The lambda's parent is its delegate
    // creation; that delegate's parent is the IArgumentOperation, whose parent is the invocation. We
    // require BOTH that the invocation targets a generated factory (static Generated.X(...)) or a
    // builder setter (`.AfterBind(…)`) — so a user helper that merely takes a Callback parameter is left
    // alone — AND that it is an event/binding callback (a Rask Callback/CallbackAsync delegate, or an
    // AfterBind* hook by name).
    private static string? CallbackNameOf(IOperation lambda)
    {
        IArgumentOperation? argument = null;
        for (IOperation? op = lambda.Parent; op is not null; op = op.Parent)
        {
            switch (op)
            {
                case IArgumentOperation arg:
                    argument = arg;
                    break;
                case IConversionOperation or IDelegateCreationOperation or IParenthesizedOperation:
                    continue; // transparent wrappers between the lambda and its argument
                default:
                    return null;
            }

            if (argument is not null)
            {
                break;
            }
        }

        if (argument?.Parameter is not { } parameter
            || argument.Parent is not IInvocationOperation call)
        {
            return null;
        }

        // On the builder surface the callback's NAME is the setter's, not the parameter's (every setter
        // takes a parameter called `value`) — `.AfterBind(_ => StateHasChanged())`.
        if (IsBuilderSetter(call.TargetMethod))
        {
            return IsRaskCallbackName(call.TargetMethod.Name) || IsRaskCallbackType(parameter.Type)
                ? call.TargetMethod.Name
                : null;
        }

        return IsGeneratedFactory(call.TargetMethod) && IsRaskCallbackParameter(parameter)
            ? parameter.Name
            : null;
    }

    private static bool IsGeneratedFactory(IMethodSymbol method) =>
        method.IsStatic && string.Equals(method.ContainingType?.Name, "Generated", StringComparison.Ordinal);

    // IOperation reports an extension invocation in its UNreduced form (the receiver is Arguments[0]),
    // so match on the containing class rather than on MethodKind.ReducedExtension.
    private static bool IsBuilderSetter(IMethodSymbol method) =>
        method.IsExtensionMethod
        && method.ContainingType?.Name.StartsWith("RaskBuilderSetters", StringComparison.Ordinal) == true;

    private static bool IsRaskCallbackParameter(IParameterSymbol parameter) =>
        IsRaskCallbackType(parameter.Type) || IsRaskCallbackName(parameter.Name);

    // Rask's named event-callback delegate types (Callback/Callback<T>/CallbackAsync/CallbackAsync<T>)
    // live in Rask.Core and are used for every On* event prop — an unambiguous signal.
    private static bool IsRaskCallbackType(ITypeSymbol type) =>
        string.Equals(type.ContainingNamespace?.ToDisplayString(), RaskCoreNamespace, StringComparison.Ordinal)
        && type.Name is "Callback" or "CallbackAsync";

    // The bound post-bind hooks are plain Action<T>/Func<T,Task>; recognise them by name.
    private static bool IsRaskCallbackName(string name) => name is "AfterBind" or "AfterBindAsync";

    private static bool InheritsFromOrIs(INamedTypeSymbol? type, INamedTypeSymbol target)
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
}
