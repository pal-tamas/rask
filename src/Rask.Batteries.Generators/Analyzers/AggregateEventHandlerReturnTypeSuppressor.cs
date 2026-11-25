using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASKSUP003 — suppresses "use the concrete return type" (<b>CA1859</b>) on an aggregate's
///     <c>static Task On(E e)</c> event handler.
/// </summary>
/// <remarks>
///     RASK179 requires the handler to answer exactly <c>Task</c>, and its natural body is one write that answers a
///     <c>Task&lt;int&gt;</c> (<c>=&gt; X.UpdateEach(…)</c>), so CA1859 asks for the very return type RASK179 refuses.
///     Only a method of the handler's exact shape (<see cref="AggregateEventHandlerGenerator.IsHandler" />) is spared;
///     any other private method still hears it.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AggregateEventHandlerReturnTypeSuppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor ConcreteReturnType = new(
        "RASKSUP003",
        "CA1859",
        "An aggregate's event handler answers Task by contract (RASK179); the generated registration calls it as one.");

    /// <inheritdoc />
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } = ImmutableArray.Create(ConcreteReturnType);

    /// <inheritdoc />
    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics.Where(d => IsOnHandler(context, d)))
        {
            context.ReportSuppression(Suppression.Create(ConcreteReturnType, diagnostic));
        }
    }

    private static bool IsOnHandler(SuppressionAnalysisContext context, Diagnostic diagnostic)
    {
        if (diagnostic.Location.SourceTree is not { } tree)
        {
            return false;
        }

        var model = context.GetSemanticModel(tree);
        var node = tree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan);
        var method = node.AncestorsAndSelf()
            .Select(n => model.GetDeclaredSymbol(n, context.CancellationToken))
            .OfType<IMethodSymbol>()
            .FirstOrDefault();

        return method is not null && AggregateEventHandlerGenerator.IsHandler(method);
    }
}
