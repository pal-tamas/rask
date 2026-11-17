using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK120 — an event callback built inside a <c>for</c> loop that reads the loop's variable.
///     <para>
///         A <c>for</c> loop has ONE variable for all its passes (unlike <c>foreach</c>, whose variable is fresh
///         per pass since C# 5), and a lambda captures the variable, not its value. A handler runs when the
///         event fires, long after the loop finished, so every row's <c>.OnClick(() =&gt; Remove(i))</c> removes
///         the row at the loop's final value. Only a lambda handed to a Rask callback step (<c>On…</c>,
///         <c>AfterBind</c>) is read — those run later by definition — and only a variable the loop's own
///         increment writes, so a lambda run on the spot (a LINQ <c>Where</c>) is never reported. Rask's
///         counterpart of Blazor's BL0014.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ForLoopCallbackCaptureAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask120 = new(
        "RASK120",
        "An event callback captures a for loop's variable",
        "The '{1}' callback reads '{0}', the for loop's variable: it runs after the loop has moved on, so it sees "
        + "the last value, not this pass's; copy it into a local inside the loop ('var item = {0};') or loop with "
        + "foreach",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        "A for loop's variable is one variable for every pass, and a lambda captures the variable rather than its "
        + "value. An event callback runs when the event fires, after the loop has finished, so a callback built "
        + "in the loop that reads the variable sees its final value in every row.",
        DiagnosticHelp.Link("RASK120"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask120);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(Analyze, OperationKind.AnonymousFunction);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var lambda = (IAnonymousFunctionOperation)context.Operation;
        var loopVariables = LoopVariablesAround(lambda);
        if (loopVariables.Count == 0 || CallbackStep.NameOf(lambda) is not { } callback)
        {
            return;
        }

        // One report per variable per callback, at its first read.
        var firstReads = lambda.Body.Descendants()
            .OfType<ILocalReferenceOperation>()
            .Where(r => loopVariables.Contains(r.Local))
            .GroupBy(r => r.Local, SymbolEqualityComparer.Default)
            .Select(g => g.First());
        foreach (var read in firstReads)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask120, read.Syntax.GetLocation(), read.Local.Name, callback));
        }
    }

    // The variables of every for loop whose BODY holds the lambda and whose increment writes them. A lambda in
    // the loop's condition or increment is not a callback built per pass, and a variable the increment leaves
    // alone does not change under the callback.
    private static HashSet<ILocalSymbol> LoopVariablesAround(IOperation lambda)
    {
        var variables = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var child = lambda;
        for (var op = lambda.Parent; op is not null; child = op, op = op.Parent)
        {
            if (op is IForLoopOperation loop && ReferenceEquals(loop.Body, child))
            {
                foreach (var local in loop.Locals.Where(l => IsWrittenBy(loop.AtLoopBottom, l)))
                {
                    variables.Add(local);
                }
            }
        }

        return variables;
    }

    private static bool IsWrittenBy(ImmutableArray<IOperation> increment, ILocalSymbol local) =>
        increment.SelectMany(static o => o.DescendantsAndSelf()).Any(o => o switch
        {
            IIncrementOrDecrementOperation { Target: ILocalReferenceOperation r } => Is(r, local),
            IAssignmentOperation { Target: ILocalReferenceOperation r } => Is(r, local),
            _ => false,
        });

    private static bool Is(ILocalReferenceOperation reference, ILocalSymbol local) =>
        SymbolEqualityComparer.Default.Equals(reference.Local, local);
}
