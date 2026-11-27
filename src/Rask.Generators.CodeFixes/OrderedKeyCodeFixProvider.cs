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
///     RASK148 — an entity's key made by Rask's ordered maker: <c>new OrderId(Guid.CreateVersion7())</c> →
///     <c>OrderId.Create()</c> for a typed id that makes its own, and otherwise <c>Guid.CreateVersion7()</c> /
///     <c>Guid.NewGuid()</c> → <c>Uuid.Create()</c>, with <c>Guid.CreateVersion7(time.GetUtcNow())</c> →
///     <c>Uuid.Create(time)</c>.
/// </summary>
/// <remarks>
///     The analyzer reports the node to replace — the whole typed id, or the Guid alone — so the fix reads it from the
///     node's shape. <c>Uuid</c> is written bare where it binds to <c>Rask.Uuid</c>, qualified where it does
///     not. A <c>CreateVersion7</c> of any other moment gets no fix: there is no time source to keep.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OrderedKeyCodeFixProvider))]
[Shared]
public sealed class OrderedKeyCodeFixProvider : RaskCodeFixProvider<ExpressionSyntax>
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK148"];

    protected override string Title => "Make the key with Rask's ordered Create()";

    protected override string EquivalenceKey => "RASK148_OrderedNew";

    protected override async Task<bool> CanFixAsync(CodeFixContext context, ExpressionSyntax node) =>
        await ReplacementAsync(context.Document, node, context.CancellationToken).ConfigureAwait(false) is not null;

    protected override async Task<Document> FixAsync(Document document, ExpressionSyntax node, CancellationToken cancellationToken) =>
        await ReplacementAsync(document, node, cancellationToken).ConfigureAwait(false) is { } replacement
            ? await ReplaceNodeAsync(document, node, replacement.WithTriviaFrom(node), cancellationToken).ConfigureAwait(false)
            : document;

    private static async Task<ExpressionSyntax?> ReplacementAsync(Document document, ExpressionSyntax node, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            return null;
        }

        switch (node)
        {
            case BaseObjectCreationExpressionSyntax creation when model.GetTypeInfo(creation, cancellationToken).Type is { } id:
                return Create(ParseExpression(id.ToMinimalDisplayString(model, node.SpanStart)));
            case InvocationExpressionSyntax { ArgumentList.Arguments.Count: 0 }:
                return Create(Uuid(model, node.SpanStart));
            case InvocationExpressionSyntax { ArgumentList.Arguments.Count: 1 } call when TimeOf(call) is { } time:
                return Create(Uuid(model, node.SpanStart), time);
            default:
                return null;
        }
    }

    // `time` of `Guid.CreateVersion7(time.GetUtcNow())`.
    private static ExpressionSyntax? TimeOf(InvocationExpressionSyntax call) =>
        call.ArgumentList.Arguments[0].Expression is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "GetUtcNow" } now,
            ArgumentList.Arguments.Count: 0,
        }
            ? now.Expression.WithoutTrivia()
            : null;

    private static InvocationExpressionSyntax Create(ExpressionSyntax on, ExpressionSyntax? time = null) =>
        InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, on, IdentifierName("Create")),
            time is null ? ArgumentList() : ArgumentList(SingletonSeparatedList(Argument(time))));

    private static ExpressionSyntax Uuid(SemanticModel model, int position) =>
        UsingDirectives.Binds(model, position, "Uuid", "Rask.Uuid")
            ? IdentifierName("Uuid")
            : ParseExpression("global::Rask.Uuid");
}
