using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.CodeFixes;

// Quick-fix for RASK026 (a StateHasChanged() the framework already does for you inside a callback the
// factory wraps): delete the statement. The message ends "remove the call", so this is that, mechanically.
//
// Only offered when the call IS the whole statement. `var x = StateHasChanged()` doesn't compile and
// can't occur, but a call used as an expression — the body of an expression-bodied lambda, say — has a
// value position to fill, and deleting it there would change what the lambda returns rather than just
// removing a no-op.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RedundantStateHasChangedCodeFixProvider))]
[Shared]
public sealed class RedundantStateHasChangedCodeFixProvider : RaskCodeFixProvider<InvocationExpressionSyntax>
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK026"];

    protected override string Title => "Remove the redundant StateHasChanged() call";

    protected override string EquivalenceKey => "RASK026_RemoveCall";

    protected override Task<bool> CanFixAsync(CodeFixContext context, InvocationExpressionSyntax node) =>
        Task.FromResult(node.Parent is ExpressionStatementSyntax { Parent: BlockSyntax });

    protected override async Task<Document> FixAsync(
        Document document,
        InvocationExpressionSyntax node,
        CancellationToken cancellationToken)
    {
        if (node.Parent is not ExpressionStatementSyntax statement)
        {
            return document;
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        // KeepNoTrivia: the statement's own leading trivia is its indentation and any comment attached to
        // it, which goes with it. Keeping it would leave a blank, indented line behind.
        var updated = root.RemoveNode(statement, SyntaxRemoveOptions.KeepNoTrivia);
        return updated is null ? document : document.WithSyntaxRoot(updated);
    }
}
