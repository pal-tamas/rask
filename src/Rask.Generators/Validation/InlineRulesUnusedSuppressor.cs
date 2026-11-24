using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Shared;

namespace Rask.Generators.Validation;

/// <summary>
///     RASKSUP002 — suppresses "unused private member" (Sonar's <b>S1144</b>, the IDE's <b>IDE0051</b>) on a type's
///     own <c>static void Rules(InlineValidator&lt;T&gt; rules)</c>.
/// </summary>
/// <remarks>
///     The convention is written private, like an entity's <c>Configure</c>: nothing in the app calls it, the
///     generated registry does, through an <c>[UnsafeAccessor]</c> no analyzer follows. Only a method of exactly the
///     convention's shape (<see cref="FluentRules.IsInlineRules" />) is spared; any other unused <c>Rules</c> still
///     warns, and is RASK139's besides.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InlineRulesUnusedSuppressor : DiagnosticSuppressor
{
    private const string Justification =
        "The generated validator registry calls a type's static Rules(InlineValidator<T>) through an [UnsafeAccessor].";

    private static readonly SuppressionDescriptor Sonar = new("RASKSUP002", "S1144", Justification);

    private static readonly SuppressionDescriptor Ide = new("RASKSUP002", "IDE0051", Justification);

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } = ImmutableArray.Create(Sonar, Ide);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics.Where(d => IsInlineRules(context, d)))
        {
            context.ReportSuppression(Suppression.Create(
                string.Equals(diagnostic.Id, Sonar.SuppressedDiagnosticId, System.StringComparison.Ordinal) ? Sonar : Ide, diagnostic));
        }
    }

    private static bool IsInlineRules(SuppressionAnalysisContext context, Diagnostic diagnostic)
    {
        if (diagnostic.Location.SourceTree is not { } tree)
        {
            return false;
        }

        var model = context.GetSemanticModel(tree);
        var node = tree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan);
        var declared = model.GetDeclaredSymbol(node, context.CancellationToken)
                       ?? node.AncestorsAndSelf().Select(n => model.GetDeclaredSymbol(n, context.CancellationToken))
                           .FirstOrDefault(static s => s is IMethodSymbol);

        return declared is IMethodSymbol method && FluentRules.IsInlineRules(method);
    }
}
