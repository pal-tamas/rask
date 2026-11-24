using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK170 — inside an aggregate, a call by the bare name lands on the operation's generated static entry rather
///     than on the method: <c>Change(id, version, details, parts)</c> written in <c>Supply</c> loads ANOTHER copy of
///     the aggregate, changes and saves it, in a transaction of its own.
/// </summary>
/// <remarks>
///     The entry is a static overload of the operation, named the same, so overload resolution is free to pick it from
///     inside the class whenever the arguments fit. Said by the type's name — <c>Supply.Change(…)</c> — the call is
///     plainly the entry; on an instance — <c>this.Change(details, parts)</c> — plainly the method.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OperationEntryCallAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rask170 = new(
        "RASK170",
        "An aggregate calls an operation's generated entry by its bare name",
        "'{1}(…)' inside '{0}' calls the generated entry '{0}.{1}(id, …)', which loads another copy of '{0}' and saves "
        + "it in a transaction of its own — call the method on the aggregate in hand ('this.{1}(…)'), or write "
        + "'{0}.{1}(…)' if another copy is what is meant",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Every public method of a partial aggregate gets a static entry of the same name — "
                     + "Supply.Change(id, version, details, parts) — that loads the aggregate, runs the method and saves. "
                     + "Inside the aggregate a call by the bare name can bind to that entry when the arguments fit, "
                     + "which is almost never what was meant: the aggregate in hand is left as it was.",
        helpLinkUri: DiagnosticHelp.Link("RASK170"));

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask170);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(Analyze, OperationKind.Invocation);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var call = (IInvocationOperation)context.Operation;
        if (call.TargetMethod is not { IsStatic: true } method ||
            call.Syntax is not InvocationExpressionSyntax { Expression: IdentifierNameSyntax } ||
            !IsEntry(method) ||
            !Inside(context.ContainingSymbol, method.ContainingType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask170, call.Syntax.GetLocation(), method.ContainingType.Name, method.Name));
    }

    internal static bool IsEntry(IMethodSymbol method) =>
        method.GetAttributes().Any(static a =>
            a.AttributeClass is { Name: "GeneratedCodeAttribute" } &&
            a.ConstructorArguments.Length > 0 &&
            string.Equals(a.ConstructorArguments[0].Value as string, OperationGenerator.Tool, StringComparison.Ordinal));

    private static bool Inside(ISymbol? at, INamedTypeSymbol type)
    {
        for (var current = at?.ContainingType; current is not null; current = current.ContainingType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, type))
            {
                return true;
            }
        }

        return false;
    }
}
