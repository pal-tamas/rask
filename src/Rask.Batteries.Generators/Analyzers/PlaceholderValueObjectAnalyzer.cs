using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK150 — a required value object of an aggregate or an entity starts from a placeholder:
///     <c>public DriverName Name { get; private set; } = DriverName.Create("Unknown");</c>.
/// </summary>
/// <remarks>
///     The initializer is what a row holds before anything sets it, so it is what a new form over the generated model
///     starts from (the field shows <c>Unknown</c>) and what the save's rules are asked about when a path forgets to
///     set it — a made-up value passing as a real one. <c>= null!</c> says what is true: there is no value until the
///     aggregate's factory or the form gives one, and a required field starts empty. A nullable property, a get-only one
///     and generated code are left alone.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PlaceholderValueObjectAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask150 = new(
        "RASK150",
        "A required value object starts from a placeholder",
        "'{0}' on '{1}' starts as a made-up '{2}': a new form shows it and the rules are asked about it — initialize it with 'null!' and set it in the factory",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A non-nullable value-object property of an aggregate or an entity whose initializer makes a value "
                     + "(X.Create(\"Unknown\")) gives every new row, and every new form over the generated model, that "
                     + "value as if someone had entered it. Initialize it with 'null!' and set it where the aggregate is made.",
        helpLinkUri: DiagnosticHelp.Link("RASK150"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask150);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.PropertyDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (declaration.Initializer is not { } initializer || IsNullForgiven(initializer.Value) ||
            context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } property ||
            property is { IsStatic: true } or { SetMethod: null } ||
            property.Type is not INamedTypeSymbol { IsReferenceType: true } held ||
            property.NullableAnnotation == NullableAnnotation.Annotated ||
            !AggregateShape.IsValueRecord(held) ||
            !AggregateShape.IsEntity(property.ContainingType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask150, initializer.Value.GetLocation(), property.Name, property.ContainingType.Name, held.Name));
    }

    // `null!` or `default!`: no value yet, said so.
    private static bool IsNullForgiven(ExpressionSyntax value) =>
        value is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } forgiven &&
        forgiven.Operand.Kind() is SyntaxKind.NullLiteralExpression or SyntaxKind.DefaultLiteralExpression;
}
