using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Rask.Generators.CodeFixes;

/// <summary>
///     RASK178 — a transaction that writes two aggregates states why, as a placeholder to fill in:
///     <c>Db.Transaction(work, manyAggregates: "…")</c>, the placeholder saying what is still owed.
/// </summary>
/// <remarks>
///     The reason goes in right after the work, named and in its own position, so a positional isolation after it still
///     binds: <c>Db.Transaction(work, manyAggregates: "…", IsolationLevel.Serializable)</c>.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ManyAggregatesReasonCodeFixProvider))]
[Shared]
public sealed class ManyAggregatesReasonCodeFixProvider : RaskCodeFixProvider<InvocationExpressionSyntax>
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK178"];

    protected override string Title => "State why the transaction writes several aggregates";

    protected override string EquivalenceKey => "RASK178_StateManyAggregates";

    protected override Task<bool> CanFixAsync(CodeFixContext context, InvocationExpressionSyntax node) =>
        Task.FromResult(node.ArgumentList.Arguments.Count > 0);

    protected override Task<Document> FixAsync(Document document, InvocationExpressionSyntax node, CancellationToken cancellationToken)
    {
        var reason = Argument(
            NameColon(IdentifierName("manyAggregates")),
            default,
            LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("TODO: why"))).WithLeadingTrivia(Space);
        var arguments = node.ArgumentList.WithArguments(node.ArgumentList.Arguments.Insert(1, reason));
        return ReplaceNodeAsync(document, node, node.WithArgumentList(arguments), cancellationToken);
    }
}
