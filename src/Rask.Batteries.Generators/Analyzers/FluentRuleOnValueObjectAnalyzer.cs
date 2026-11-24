using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK181 — a FluentValidation rule on an aggregate or entity rules a member that is a value object:
///     <c>rules.RuleFor(s =&gt; s.Name).MaximumLength(200)</c> where <c>Name</c> is a <c>SupplierName</c>.
/// </summary>
/// <remarks>
///     A value object checks its own rules wherever one is made, so a rule on the member restates them in a second place,
///     where they can drift apart, and is never asked when the value object is made anywhere else. Only whether the member
///     is there at all (<c>NotNull</c>, <c>NotEmpty</c>) is the owner's to say. Read in the in-type
///     <c>Rules(InlineValidator&lt;T&gt;)</c> and in an <c>AbstractValidator&lt;T&gt;</c> alike. Silent unless the
///     compilation references FluentValidation.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FluentRuleOnValueObjectAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask181 = new(
        "RASK181",
        "A FluentValidation rule rules a value object",
        "'{0}.{1}' is a '{2}', whose own rules guard it — move the rule into '{2}': its base arguments, its Rules() override, or its own static Rules(InlineValidator<{2}>)",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A value object declared from a Rask.Values base record checks its rules as it is made, wherever "
                     + "that is. A FluentValidation rule on an aggregate's value-object member restates them on one owner "
                     + "only, so the two drift apart and a value made anywhere else is never asked. Write the rule in the "
                     + "value object itself. NotNull and NotEmpty, which say only that the member is given, are not "
                     + "reported.",
        helpLinkUri: DiagnosticHelp.Link("RASK181"));

    // Calls that say the member is given, or shape the rule without checking anything: never the value object's to own.
    private static readonly ImmutableHashSet<string> OwnersCalls = ImmutableHashSet.Create(
        System.StringComparer.Ordinal,
        "NotNull", "NotEmpty", "WithMessage", "WithName", "WithErrorCode", "WithSeverity", "WithState", "OverridePropertyName",
        "When", "Unless", "WhenAsync", "UnlessAsync", "Cascade", "DependentRules");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask181);

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

            start.RegisterOperationAction(Analyze, OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var ruleFor = (IInvocationOperation)context.Operation;
        if (FluentRules.ValidatedBy(ruleFor) is not INamedTypeSymbol owner ||
            !AggregateShape.IsEntity(owner) ||
            FluentRules.RuledProperty(ruleFor) is not { } member ||
            AggregateShape.ValueObjectTypeOf(member.Type) is not { } valueObject ||
            !AggregateShape.IsValueRecord(valueObject) ||
            FluentRules.CallsOn(ruleFor).All(static call => OwnersCalls.Contains(call.TargetMethod.Name)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask181, ruleFor.Syntax.GetLocation(), owner.Name, member.Name, valueObject.Name));
    }
}
