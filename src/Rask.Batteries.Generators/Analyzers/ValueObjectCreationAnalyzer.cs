using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK127 — a value object declared from a Rask.Data base record is made with <c>new</c> or copied with
///     <c>with</c> outside its own type.
/// </summary>
/// <remarks>
///     <c>record KmRange(int Start, int End) : Range(Start, End)</c> checks its rules in its constructor, so <c>new</c>
///     throws for a refused value without saying which field it came from, and <c>with</c> copies one past its rules
///     altogether. The generated <c>KmRange.Create(…, field)</c>, <c>TryCreate</c> and <c>CreateOptional</c> are the
///     way in. Tests are analyzed too. Generated code is not: the generated half uses the constructor on purpose.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ValueObjectCreationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask127 = new(
        "RASK127",
        "A value object declared from a base record is made past its rules",
        "'{0}' is declared from Rask.Data.{1}, so it is made only by '{0}.Create(…)', which checks its rules under the field it names — {2}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A value object declared from Value, Text, Number, Whole, Day or Range is made through its generated "
                     + "Create, TryCreate or CreateOptional, which check the rules it declares and name the field a "
                     + "refusal belongs to. 'new' throws without the field, and 'with' copies a value past its rules. "
                     + "Inside its own type both are allowed; EF Core loads stored values without asking the rules.",
        helpLinkUri: DiagnosticHelp.Link("RASK127"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask127);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        // A syntax action, not an operation one: a `new` that does not bind (a wrong argument) still names the type,
        // and is still the line the quick-fix turns into Create.
        context.RegisterSyntaxNodeAction(
            AnalyzeNew, SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression);
        context.RegisterOperationAction(AnalyzeWith, OperationKind.With);
    }

    private static void AnalyzeNew(SyntaxNodeAnalysisContext context)
    {
        if (context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken).Type is INamedTypeSymbol created &&
            Outside(context.ContainingSymbol, created) is { } valueBase)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rask127, context.Node.GetLocation(), created.Name, valueBase, "call it instead of 'new'"));
        }
    }

    private static void AnalyzeWith(OperationAnalysisContext context)
    {
        var copy = (IWithOperation)context.Operation;
        if (copy.Type is INamedTypeSymbol copied && Outside(context.ContainingSymbol, copied) is { } valueBase)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rask127, copy.Syntax.GetLocation(), copied.Name, valueBase, "make a new one with it instead of 'with'"));
        }
    }

    // The base record the type is declared from, when the operation is outside the type; null otherwise.
    private static string? Outside(ISymbol? where, INamedTypeSymbol type)
    {
        if (!AggregateShape.IsValueRecord(type))
        {
            return null;
        }

        for (var container = where as INamedTypeSymbol ?? where?.ContainingType; container is not null; container = container.ContainingType)
        {
            if (SymbolEqualityComparer.Default.Equals(container, type))
            {
                return null;
            }
        }

        return AggregateShape.DeclaredValueBaseOf(type) ?? "Value";
    }
}
