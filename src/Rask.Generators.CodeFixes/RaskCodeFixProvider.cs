using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Rask.Generators.CodeFixes;

// Shared skeleton for the Rask code fixes: locate the nearest enclosing <typeparamref name="TNode"/>
// at the diagnostic span, optionally gate on a semantic check, then register a single CodeAction that
// rewrites the document. Keeps each concrete provider down to its title and the actual edit.
public abstract class RaskCodeFixProvider<TNode> : CodeFixProvider
    where TNode : SyntaxNode
{
    protected abstract string Title { get; }

    protected abstract string EquivalenceKey { get; }

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var diagnostic = context.Diagnostics[0];
        if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<TNode>() is not { } node)
        {
            return;
        }

        if (!await CanFixAsync(context, node).ConfigureAwait(false))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => FixAsync(context.Document, node, ct), EquivalenceKey),
            diagnostic);
    }

    // Semantic guard hook — return false to withhold the fix (e.g. when applying it would trade the
    // current diagnostic for a worse one). Default: always offer.
    protected virtual Task<bool> CanFixAsync(CodeFixContext context, TNode node) => Task.FromResult(true);

    protected abstract Task<Document> FixAsync(Document document, TNode node, CancellationToken cancellationToken);

    // Shared helper: swap a node for its rewritten form and return the updated document.
    private protected static async Task<Document> ReplaceNodeAsync(
        Document document, SyntaxNode oldNode, SyntaxNode newNode, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null ? document : document.WithSyntaxRoot(root.ReplaceNode(oldNode, newNode));
    }
}
