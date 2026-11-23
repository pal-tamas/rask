using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK127 — a value object declared from a base record is made with <c>new</c> or changed with <c>with</c>
///     outside its own type.
/// </summary>
/// <remarks>
///     <c>record DriverName() : Text(MaxLength: 1000)</c> has a public parameterless constructor because C# gives a
///     record with a primary constructor one, and it builds an empty name that no rule was asked about. The generated
///     <c>DriverName.Create(value)</c> is the way in; a <c>with</c> copies the value past its rules the same way.
///     Generated code is not analyzed: the generated half uses the constructor on purpose.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ValueObjectCreationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask127 = new(
        "RASK127",
        "A value object declared from a base record is made past its rules",
        "'{0}' is declared from Rask.Data.{1}, so it is made only by '{0}.Create(…)', which checks its rules — {2}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A value object declared from Text, Number, Whole or Day is made through its generated Create, "
                     + "TryCreate or CreateOptional, which check the rules it declares. 'new' builds one no rule was "
                     + "asked about, and 'with' copies one past them. Inside its own type both are allowed; EF Core "
                     + "rebuilds stored values through the generated private constructor and is not affected.",
        helpLinkUri: DiagnosticHelp.Link("RASK127"));

    /// <summary>Set on a diagnostic about a <c>with</c>, for which there is no quick-fix.</summary>
    internal const string NoFixProperty = "RaskNoFix";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask127);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        // A syntax action, not an operation one: `new DriverName("Ada")` reaches the generated PRIVATE constructor, so
        // outside the type it does not bind (CS0122) and there is no creation operation — yet it is exactly the
        // line the quick-fix turns into DriverName.Create("Ada").
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
                Rask127,
                copy.Syntax.GetLocation(),
                ImmutableDictionary<string, string?>.Empty.Add(NoFixProperty, ""),
                copied.Name,
                valueBase,
                "make a new one with it instead of 'with'"));
        }
    }

    // The base record the type is declared from, when the operation is outside the type; null otherwise.
    private static string? Outside(ISymbol? where, INamedTypeSymbol type)
    {
        if (AggregateShape.ValueBaseOf(type) is not { } valueBase)
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

        return valueBase;
    }
}
