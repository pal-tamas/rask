using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK184 — an aggregate or an entity throws <c>new InvalidOperationException(…)</c> or
///     <c>new ArgumentException(…)</c>: a rule said no in a way no form can show.
/// </summary>
/// <remarks>
///     A form shows a <c>RaskValidationException</c> under the field it names, or above the form; anything else is a
///     failed request the person sees as an error. <c>Refuse.When(…)</c> / <c>Refuse.Unless(…)</c> (Rask.Data) say the
///     same sentence where it is read. Only those two exact types are reported: a subclass
///     (<c>ArgumentNullException</c>, <c>ArgumentOutOfRangeException</c>) is a guard against a caller's mistake, not a
///     rule. A broken invariant — a programming error, never a rule the person at the screen could act on — throws
///     <c>System.Diagnostics.UnreachableException</c>, which is not reported either. Reported only when the compilation
///     has <c>Rask.Data.Refuse</c>, so the quick-fix always has somewhere to point. A suggestion by default; a warning
///     under <c>&lt;RaskStrict&gt;</c>.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RuleExceptionAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask184 = new(
        "RASK184",
        "An aggregate refuses with an exception no form can show",
        "'{0}' thrown in '{1}' reaches the person as a failed request, not under a field — refuse with 'Refuse.When(…)' or 'Refuse.Unless(…)' (Rask.Data)",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A rule of an aggregate or an entity that says no is a refusal for the person at the screen: "
                     + "Refuse.When(condition, message) throws a RaskValidationException, which a form shows under the "
                     + "field it names or above the form. An InvalidOperationException or an ArgumentException is a "
                     + "failed request instead. A broken invariant, a programming error no one at the screen can act "
                     + "on, throws System.Diagnostics.UnreachableException, which is not reported. A suggestion by "
                     + "default; a warning under <RaskStrict>.",
        helpLinkUri: DiagnosticHelp.Link("RASK184"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask184);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName("Rask.Data.Refuse") is null)
            {
                return;
            }

            start.RegisterOperationAction(Analyze, OperationKind.Throw);
        });
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var thrown = (IThrowOperation)context.Operation;
        if (Unwrapped(thrown.Exception) is IObjectCreationOperation { Type: INamedTypeSymbol type } creation &&
            IsRuleException(type) &&
            DomainCode.EntityOf(context.ContainingSymbol) is { } entity)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask184, creation.Syntax.GetLocation(), type.Name, entity.Name));
        }
    }

    private static IOperation? Unwrapped(IOperation? operation) =>
        operation is IConversionOperation { IsImplicit: true, Operand: var operand } ? operand : operation;

    private static bool IsRuleException(INamedTypeSymbol type) =>
        type is { Name: "InvalidOperationException" or "ArgumentException", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };
}
