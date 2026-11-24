using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Shared;

namespace Rask.Generators.Validation;

/// <summary>
///     RASK139 — a method named <c>Rules</c> takes an <c>InlineValidator&lt;X&gt;</c> but is not the in-type convention
///     <c>static void Rules(InlineValidator&lt;T&gt; rules)</c>, so its rules are never registered.
/// </summary>
/// <remarks>
///     The registration finds the convention by its shape alone: static, returning nothing, one parameter, not generic,
///     validating the very type it is declared in, and that type not generic (generated code could not name it). Any
///     other <c>Rules</c> taking an <c>InlineValidator</c> reads like rules and never runs. Silent unless the
///     compilation references FluentValidation.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InlineRulesShapeAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask180 = new(
        "RASK139",
        "A Rules method is not the in-type FluentValidation convention",
        "'{0}.Rules' is never registered — {1}: the in-type rules are 'static void Rules(InlineValidator<{0}> rules)' on a non-generic type",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "FluentValidation rules written inside a type are found by their shape: "
                     + "static void Rules(InlineValidator<T> rules), any accessibility, where T is the type the method is "
                     + "declared in and neither is generic. A Rules method taking an InlineValidator in any other shape — "
                     + "an instance method, one returning a value, taking more parameters, generic, validating another "
                     + "type, or declared in a generic type — is not registered, so its rules never run.",
        helpLinkUri: DiagnosticHelp.Link("RASK139"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask180);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(FluentRules.InlineValidatorName) is null)
            {
                return;
            }

            start.RegisterSymbolAction(Analyze, SymbolKind.Method);
        });
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method is not { Name: FluentRules.RulesMethod, MethodKind: MethodKind.Ordinary } ||
            !method.Parameters.Any(static p => FluentRules.IsInlineValidator(p.Type, out _)) ||
            FluentRules.IsInlineRules(method) ||
            WhyNot(method) is not { } why)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask180, method.Locations.FirstOrDefault(), method.ContainingType.Name, why));
    }

    // The first way the method misses the convention, as the message says it.
    private static string? WhyNot(IMethodSymbol method)
    {
        if (FluentRules.IsGenericOrInGeneric(method.ContainingType))
        {
            return "'" + method.ContainingType.Name + "' is generic, so the registration cannot name it";
        }

        if (method.IsGenericMethod)
        {
            return "it is generic";
        }

        if (!method.IsStatic)
        {
            return "it is not static";
        }

        if (!method.ReturnsVoid)
        {
            return "it returns '" + method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + "'";
        }

        if (method.Parameters.Length != 1)
        {
            return "it takes other parameters";
        }

        return FluentRules.IsInlineValidator(method.Parameters[0].Type, out var validated)
            ? "it validates '" + validated!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + "', not '" + method.ContainingType.Name + "'"
            : null;
    }
}
