using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK178 — a <c>Db.Transaction(…)</c> whose work writes two different aggregates and states no
///     <c>manyAggregates:</c> reason.
/// </summary>
/// <remarks>
///     <para>
///         An aggregate is the transaction boundary: two committed together is a decision, written down where it is made
///         — <c>Db.Transaction(async () =&gt; …, manyAggregates: "why")</c> — or undone (the second written through a
///         domain event). At run time <c>RaskDataOptions.OneAggregatePerTransaction</c> meets the same transaction;
///         this says so at the line, before it runs.
///     </para>
///     <para>
///         A write is a call, anywhere in the work (nested lambdas too), to a generated write of an aggregate: its
///         <c>Create</c>, <c>Update</c>, <c>Delete</c>, an instance's <c>Save()</c> or <c>Delete()</c>, or an operation
///         entry — the static generated into the aggregate beside a public instance method of the same name
///         (<c>Supply.Change(id, …)</c>), or beside a static that returns the aggregate (a factory). Reads are not writes. One type
///         written twice is not reported: the analyzer cannot tell two rows apart, and the run-time check can.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ManyAggregatesTransactionAnalyzer : DiagnosticAnalyzer
{
    private const string Reason = "manyAggregates";

    private static readonly DiagnosticDescriptor Rask178 = new(
        "RASK178",
        "A transaction writes two aggregates without saying why",
        "This Db.Transaction writes '{0}' and '{1}' and states no manyAggregates: reason — an aggregate is the transaction boundary: write one per transaction, or say why they are only valid together",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A Db.Transaction whose work writes two different aggregates commits them as one, which is a "
                     + "design decision: state it with Db.Transaction(async () => …, manyAggregates: \"why\"), or write "
                     + "the second through a domain event. RaskDataOptions.OneAggregatePerTransaction meets the same "
                     + "transaction at run time. A suggestion by default; a warning under <RaskStrict>.",
        helpLinkUri: DiagnosticHelp.Link("RASK178"));

    private static readonly ImmutableHashSet<string> Writes = ImmutableHashSet.Create(
        System.StringComparer.Ordinal, "Create", "Update", "Delete", "Save");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask178);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName("Rask.Data.Db") is not { } db)
            {
                return;
            }

            start.RegisterOperationAction(context => Analyze(context, db), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol db)
    {
        var call = (IInvocationOperation)context.Operation;
        if (call.TargetMethod is not { Name: "Transaction", IsStatic: true } transaction ||
            !SymbolEqualityComparer.Default.Equals(transaction.ContainingType, db) ||
            transaction.Parameters.Any(static p => string.Equals(p.Name, Reason, System.StringComparison.Ordinal)) ||
            call.Arguments.FirstOrDefault(static a => a.Parameter?.Ordinal == 0) is not { } work)
        {
            return;
        }

        var written = WrittenAggregates(work.Value).Take(2).ToList();
        if (written.Count == 2)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask178, call.Syntax.GetLocation(), written[0].Name, written[1].Name));
        }
    }

    // Each aggregate type the work writes, once, in the order first written.
    private static IEnumerable<ITypeSymbol> WrittenAggregates(IOperation work)
    {
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var call in work.DescendantsAndSelf().OfType<IInvocationOperation>())
        {
            if (WrittenBy(call.TargetMethod) is { } aggregate && seen.Add(aggregate))
            {
                yield return aggregate;
            }
        }
    }

    // The aggregate a generated write belongs to, or null for any other call: a write of an extension block over it
    // (`Tank.Create(…)`, `tank.Save()`), or an entry the operation generator writes into the aggregate's own class.
    private static ITypeSymbol? WrittenBy(IMethodSymbol method)
    {
        var extension = method.ContainingType.IsExtension;
        var owner = extension ? method.ContainingType.ExtensionParameter?.Type : method.ContainingType;
        if (owner is not INamedTypeSymbol aggregate || !AggregateShape.IsAggregate(aggregate))
        {
            return null;
        }

        if (Writes.Contains(method.Name) && (extension || method.IsStatic))
        {
            return aggregate;
        }

        return !extension && method.IsStatic && IsOperationEntry(aggregate, method) ? aggregate : null;
    }

    // A static beside an instance method of its name (an operation), or beside a static that returns the aggregate (a
    // factory, written non-public): the entry that loads or builds it, runs the method and saves.
    private static bool IsOperationEntry(INamedTypeSymbol aggregate, IMethodSymbol entry) =>
        !ReturnsAggregate(entry, aggregate) &&
        aggregate.GetMembers(entry.Name).OfType<IMethodSymbol>().Any(m =>
            m.MethodKind == MethodKind.Ordinary && !SymbolEqualityComparer.Default.Equals(m, entry) &&
            ((!m.IsStatic && m.DeclaredAccessibility == Accessibility.Public) || (m.IsStatic && ReturnsAggregate(m, aggregate))));

    private static bool ReturnsAggregate(IMethodSymbol method, INamedTypeSymbol aggregate)
    {
        var returned = method.ReturnType is INamedTypeSymbol { Name: "Task" or "ValueTask", TypeArguments.Length: 1 } task
            ? task.TypeArguments[0]
            : method.ReturnType;
        return SymbolEqualityComparer.Default.Equals(returned, aggregate);
    }
}
