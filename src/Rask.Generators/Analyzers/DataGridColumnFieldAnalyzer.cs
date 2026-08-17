using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

// RASK034 — warn when a BsDataGrid uses the column chooser or reordering, but a column in its inline Columns
// list sets no Field. The chooser and ColumnOrder address a column by the token read off Field (its
// URL-serialisable name); a column with no Field has no token, so it silently can never be shown/hidden or
// reordered — it just sits pinned with no menu row. Best-effort: only inline `Columns: [ new BsColumn... ]`
// collection expressions are inspected (a variable's contents are out of reach), and the warning is
// suppressible. A column that opts out of BOTH axes (Hideable = false and Reorderable = false) is a deliberate
// fixture and is not flagged.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DataGridColumnFieldAnalyzer : DiagnosticAnalyzer
{
    private const string GeneratedClassName = "Generated";
    private const string FactoryName = "BsDataGrid";
    private const string ColumnTypeName = "BsColumn";

    // The parameters whose presence means the chooser/reorder feature is in use.
    private static readonly string[] ChooserArguments =
    [
        "ColumnChooser", "HiddenColumns", "ColumnOrder", "OnHiddenColumnsChange", "OnHiddenColumnsChangeAsync",
        "OnColumnOrderChange", "OnColumnOrderChangeAsync",
    ];

    private static readonly DiagnosticDescriptor Rask034 = new(
        "RASK034",
        "Column has no Field for the chooser",
        "This column sets no Field, so the BsDataGrid column chooser can't show/hide or reorder it — "
        + "add Field = r => r.X to name it, or opt it out with Hideable = false and Reorderable = false",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        "The column chooser and ColumnOrder address a column by the token read off its Field expression. A "
        + "column with no Field has no token, so it can never be hidden or reordered — it stays pinned with no "
        + "menu row, silently. Give it a Field, or make it a deliberate fixture with Hideable = false and "
        + "Reorderable = false.",
        DiagnosticHelp.Link("RASK034"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask034);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);

        // The chain reaches none of the above: its steps are extension methods on Build<T>, not a static
        // Generated.BsDataGrid(...), so the factory branch matched no chain and a column that can never be
        // shown, hidden or reordered went unreported.
        context.RegisterOperationAction(AnalyzeChain, OperationKind.Invocation);
    }

    private static void AnalyzeChain(OperationAnalysisContext context)
    {
        var operation = (IInvocationOperation)context.Operation;

        // Only the OUTERMOST link, so the chain is read once and as a whole — `.Columns(…)` and the step
        // that turns the chooser on can sit in either order.
        if (operation.Syntax.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax })
        {
            return;
        }

        if (operation.TargetMethod.IsStatic
            && string.Equals(operation.TargetMethod.ContainingType?.Name, GeneratedClassName, StringComparison.Ordinal))
        {
            return; // The factory branch owns this one.
        }

        if (BuiltComponent(operation.Type) is not { Name: FactoryName })
        {
            return;
        }

        IInvocationOperation? columnsStep = null;
        var usesChooser = false;

        for (var step = operation; step is not null; step = NextStep(step))
        {
            var name = step.TargetMethod.Name;
            if (string.Equals(name, "Columns", StringComparison.Ordinal))
            {
                columnsStep = step;
            }
            else if (Array.IndexOf(ChooserArguments, name) >= 0)
            {
                usesChooser = true;
            }
        }

        // The feature has to be in use — otherwise a missing Field is fine — and the columns have to be
        // written inline, exactly as in the factory branch (a variable's contents are out of reach).
        if (!usesChooser
            || columnsStep is null
            || StepArgument(columnsStep) is not CollectionExpressionSyntax columns)
        {
            return;
        }

        foreach (var element in columns.Elements)
        {
            if (element is ExpressionElementSyntax { Expression: BaseObjectCreationExpressionSyntax creation }
                && context.Operation.SemanticModel?.GetTypeInfo(creation, context.CancellationToken).Type
                    is { Name: ColumnTypeName }
                && !SetsField(creation)
                && !IsPinnedFixture(creation))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rask034, creation.GetLocation()));
            }
        }
    }

    // A step's value is its first real argument — index 1 when it is written as an extension method,
    // because argument 0 is the Build<T> receiver.
    private static ExpressionSyntax? StepArgument(IInvocationOperation step)
    {
        var index = step.TargetMethod.IsExtensionMethod ? 1 : 0;
        return step.Arguments.Length > index ? step.Arguments[index].Value.Syntax as ExpressionSyntax : null;
    }

    // The component a Build<T> is building, or null when the type is anything else.
    private static INamedTypeSymbol? BuiltComponent(ITypeSymbol? type) =>
        type is INamedTypeSymbol { IsGenericType: true, Arity: 1 } named
        && string.Equals(named.ConstructedFrom.ToDisplayString(), "Rask.Core.Build<T>", StringComparison.Ordinal)
            ? named.TypeArguments[0] as INamedTypeSymbol
            : null;

    // The next link DOWN the chain: a step's receiver is whatever produced the Build<T> it extends,
    // written either as an extension method (argument 0) or as an instance call.
    private static IInvocationOperation? NextStep(IInvocationOperation operation)
    {
        var receiver = operation.Instance
                       ?? (operation.TargetMethod.IsExtensionMethod && operation.Arguments.Length != 0
                           ? operation.Arguments[0].Value
                           : null);

        while (receiver is IParenthesizedOperation or IConversionOperation { IsImplicit: true })
        {
            receiver = receiver is IParenthesizedOperation p ? p.Operand : ((IConversionOperation)receiver).Operand;
        }

        return receiver as IInvocationOperation;
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // A BsDataGrid factory call: a static method named BsDataGrid on a class named Generated.
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol
            {
                IsStatic: true, Name: FactoryName, ContainingType.Name: GeneratedClassName,
            })
        {
            return;
        }

        // The feature has to be in use — otherwise a missing Field is fine.
        if (!UsesChooser(invocation))
        {
            return;
        }

        // The Columns argument, inline only. Named `Columns:` or the second positional argument.
        if (ColumnsArgument(invocation) is not CollectionExpressionSyntax columns)
        {
            return;
        }

        foreach (var element in columns.Elements)
        {
            if (element is ExpressionElementSyntax { Expression: BaseObjectCreationExpressionSyntax creation }
                && IsColumn(context, creation)
                && !SetsField(creation)
                && !IsPinnedFixture(creation))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rask034, creation.GetLocation()));
            }
        }
    }

    private static bool UsesChooser(InvocationExpressionSyntax invocation)
    {
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.NameColon?.Name.Identifier.ValueText is { } name
                && Array.IndexOf(ChooserArguments, name) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static ExpressionSyntax? ColumnsArgument(InvocationExpressionSyntax invocation)
    {
        var arguments = invocation.ArgumentList.Arguments;
        foreach (var arg in arguments)
        {
            if (string.Equals(arg.NameColon?.Name.Identifier.ValueText, "Columns", StringComparison.Ordinal))
            {
                return arg.Expression;
            }
        }

        // Positional fall-back: Columns is the second factory parameter, and positional args precede named ones.
        return arguments.Count > 1 && arguments[1].NameColon is null ? arguments[1].Expression : null;
    }

    private static bool IsColumn(SyntaxNodeAnalysisContext context, BaseObjectCreationExpressionSyntax creation) =>
        context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type is { Name: ColumnTypeName };

    private static bool SetsField(BaseObjectCreationExpressionSyntax creation) =>
        Assigns(creation, "Field");

    // A column that opts out of both hiding and reordering is a deliberate fixture — no Field needed.
    private static bool IsPinnedFixture(BaseObjectCreationExpressionSyntax creation) =>
        AssignsFalse(creation, "Hideable") && AssignsFalse(creation, "Reorderable");

    private static bool Assigns(BaseObjectCreationExpressionSyntax creation, string member) =>
        creation.Initializer is not null
        && creation.Initializer.Expressions.Any(e =>
            e is AssignmentExpressionSyntax { Left: IdentifierNameSyntax id }
            && string.Equals(id.Identifier.ValueText, member, StringComparison.Ordinal));

    private static bool AssignsFalse(BaseObjectCreationExpressionSyntax creation, string member) =>
        creation.Initializer is not null
        && creation.Initializer.Expressions.Any(e =>
            e is AssignmentExpressionSyntax
            {
                Left: IdentifierNameSyntax id, Right: LiteralExpressionSyntax lit,
            }
            && string.Equals(id.Identifier.ValueText, member, StringComparison.Ordinal)
            && lit.IsKind(SyntaxKind.FalseLiteralExpression));
}
