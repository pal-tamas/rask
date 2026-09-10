using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK076 — a grid column with no field token, in a grid that lets the reader hide, reorder or
///     group columns by name.
///     <para>
///         A column opened with <c>Column()</c> has no field, which is exactly what an actions column
///         wants. The token is also the only name any of those three menus has for a column, so a
///         token-less one can be SHOWN but never hidden, moved or grouped: the chooser simply has no row
///         for it, and the reader is left looking for a control that was never rendered.
///     </para>
///     <para>
///         The successor to RASK034, which said the same thing about <c>BsDataGrid</c> and retired with
///         <c>Rask.Bootstrap</c>. Worth knowing that RASK034 never actually fired once the grid moved to
///         a chain, so this one is tested on the shape it has to catch rather than only on compiling.
///     </para>
/// </summary>
/// <remarks>
///     <para>
///         Walks the chain's invocation operations directly rather than going through
///         <see cref="BuilderEntry.TryReadChain" />: that helper wants a <c>Build&lt;T&gt;</c> entry, and
///         the grid's receiver is the component itself, so reading the chain that way would find nothing
///         and the diagnostic would be silently dead — which is how RASK022/023/025/038/044 each spent a
///         while, and what RASK034 did for its whole life.
///     </para>
///     <para>
///         Reported at the <c>Column()</c> call, which is the shorter span and the one the author can act
///         on. Only the common inline form is walkable — a column built by a helper method, or a grid
///         reached through a local, is out of view and deliberately not guessed at.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GridColumnTokenAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask076 = new(
        "RASK076",
        "Grid column with no field token",
        "This column names no field, so it has no token — the grid's {0} can never {1} it. Open it with Field(...) to give it one, or {2} to say it is deliberately fixed.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A grid identifies a column by the field token it was opened with. Column() deliberately "
                     + "has none, which is right for an actions column -- but the column chooser, the group "
                     + "panel, HiddenColumns, ColumnOrder and Grouped all address columns BY that token, so a "
                     + "token-less column can be shown and never hidden, moved or grouped. Nothing fails: the "
                     + "menu is simply missing a row, which reads as a bug in the grid rather than in the call "
                     + "site. Saying the column is fixed -- Hideable(false), Reorderable(false), "
                     + "Groupable(false) -- states the same intent where a reader can see it, and silences this.",
        helpLinkUri: DiagnosticHelp.Link("RASK076"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask076);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(Analyze, OperationKind.Invocation);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var operation = (IInvocationOperation)context.Operation;

        // The token-less opening, and only it. Field(...) always produces a token, so it is never this.
        if (operation.TargetMethod.Name != "Column" || !IsGrid(operation.TargetMethod.ContainingType))
        {
            return;
        }

        // What this column has said about itself, reading UP from Column() through the steps applied to
        // it: `c.Column().Hideable(false).Reorderable(false)`.
        var optedOut = OptOuts(operation);

        // The grid this column belongs to is the receiver of the indexer the column lambda was handed
        // to. Walking OUT to the nearest element access is not enough -- a cell template is full of
        // them -- so the ancestors are searched for one whose receiver is actually a grid.
        var grid = EnclosingGridChain(operation, context.Operation.SemanticModel);
        if (grid is null)
        {
            return;
        }

        foreach (var axis in Axes)
        {
            if (!axis.EnabledBy(grid) || optedOut.Contains(axis.OptOut))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rask076, operation.Syntax.GetLocation(), axis.Feature, axis.Verb, axis.OptOutCall));
            return;
        }
    }

    // The three things a token addresses, each with the steps that turn it on and the one step that says
    // this column is deliberately not part of it. ColumnChooser drives BOTH hiding and reordering -- the
    // grid's own ReorderEnabled reads `ColumnChooser is true || OrderControlled` -- so a column under a
    // chooser has to opt out of both to be silent, exactly as RASK034 required.
    private static readonly Axis[] Axes =
    [
        new("column chooser", "hide", "Hideable(false)", "Hideable", ["ColumnChooser", "HiddenColumns"]),
        new("column ordering", "move", "Reorderable(false)", "Reorderable", ["ColumnChooser", "ColumnOrder"]),
        new("grouping", "group", "Groupable(false)", "Groupable", ["GroupPanel", "Grouped"])
    ];

    private sealed record Axis(
        string Feature,
        string Verb,
        string OptOutCall,
        string OptOut,
        string[] Steps)
    {
        public bool EnabledBy(IInvocationOperation grid)
        {
            for (var current = grid; current is not null; current = Next(current))
            {
                if (System.Array.IndexOf(Steps, current.TargetMethod.Name) >= 0 && !IsOff(current))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Every `X(false)` this column applied to itself. A non-literal argument is not knowable here, and
    // is treated as no opt-out rather than guessed at either way.
    private static ImmutableHashSet<string> OptOuts(IInvocationOperation column)
    {
        var found = ImmutableHashSet.CreateBuilder<string>(System.StringComparer.Ordinal);

        for (var current = Outermost(column); current is not null; current = Next(current))
        {
            if (IsOff(current))
            {
                found.Add(current.TargetMethod.Name);
            }

            if (current == column)
            {
                break;
            }
        }

        return found.ToImmutable();
    }

    private static IInvocationOperation? EnclosingGridChain(IInvocationOperation column, SemanticModel? model)
    {
        if (model is null)
        {
            return null;
        }

        for (var node = column.Syntax.Parent; node is not null; node = node.Parent)
        {
            if (node is not ElementAccessExpressionSyntax access)
            {
                continue;
            }

            if (Unwrap(model.GetOperation(access.Expression)) is IInvocationOperation receiver
                && IsGrid(receiver.TargetMethod.ReturnType))
            {
                return receiver;
            }
        }

        return null;
    }

    // The kit's grid, by type rather than by name: a component of somebody else's with a method spelled
    // `Column` is none of this analyzer's business.
    private static bool IsGrid(ITypeSymbol? type) =>
        type is INamedTypeSymbol named
        && named.ConstructedFrom.ToDisplayString() is "Rask.Ui.UiDataGrid<T, TKey>";

    // A literal `false` or `null`. Anything else -- a variable, an expression, or nothing at all -- is
    // not knowable here, and silence would be a guess in the other direction.
    private static bool IsOff(IInvocationOperation operation)
    {
        if (operation.Arguments.Length == 0)
        {
            return false;
        }

        var value = Unwrap(operation.Arguments[operation.Arguments.Length - 1].Value);
        if (value is null)
        {
            return false;
        }

        return value.ConstantValue is { HasValue: true, Value: false or null };
    }

    // The outermost call of the chain this one belongs to.
    //
    // Walking UP has to step over the wrappers the operation tree puts between a call and the call it is
    // the receiver of. A chain step is written as an EXTENSION setter, so its receiver is argument 0 --
    // and an argument is an IArgumentOperation, not the invocation itself. Stopping at the first parent
    // that is not an invocation therefore finds nothing at all, which is what made `Hideable(false)`
    // invisible from the Column() this starts at.
    private static IInvocationOperation Outermost(IInvocationOperation operation)
    {
        var current = operation;

        while (true)
        {
            var parent = current.Parent;
            while (parent is IArgumentOperation
                   or IParenthesizedOperation
                   or IConversionOperation { IsImplicit: true })
            {
                parent = parent.Parent;
            }

            if (parent is not IInvocationOperation invocation || Next(invocation) != current)
            {
                return current;
            }

            current = invocation;
        }
    }

    // The next link DOWN the chain: a setter's receiver is the invocation that produced it, whether it
    // was written as an extension (argument 0) or as an instance call.
    private static IInvocationOperation? Next(IInvocationOperation operation)
    {
        var receiver = operation.Instance
                       ?? (operation.TargetMethod.IsExtensionMethod && operation.Arguments.Length != 0
                           ? operation.Arguments[0].Value
                           : null);

        return Unwrap(receiver) as IInvocationOperation;
    }

    // Parentheses, an implicit conversion (bool to bool?) and a null-forgiving `!` are all part of the
    // same expression, so they pass straight through.
    private static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IParenthesizedOperation or IConversionOperation { IsImplicit: true })
        {
            operation = operation is IParenthesizedOperation p
                ? p.Operand
                : ((IConversionOperation)operation).Operand;
        }

        return operation;
    }
}
