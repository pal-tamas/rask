using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.CodeFixes;

// Quick-fix for RASK095 (a chain skips a required step): the missing steps go right after the half-built
// chain — `Card.Note("x")` → `Card.Title("").Note("x")`. A step the chain already takes further along is
// MOVED there with its argument rather than given a placeholder: after the component exists a required
// property has no setter, so `Card.Note("x").Title("t")` becomes `Card.Title("t").Note("x")`.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IncompleteChainCodeFixProvider))]
[Shared]
public sealed class IncompleteChainCodeFixProvider : CodeFixProvider
{
    private const string StepsKey = "Steps";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("RASK095");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        if (root?.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not { } found
            || found.FirstAncestorOrSelf<ExpressionSyntax>(e => e.Span == diagnostic.Location.SourceSpan)
                is not { } chain
            || !diagnostic.Properties.TryGetValue(StepsKey, out var steps)
            || steps is null
            || Rewrite(chain, Parse(steps)) is not { } rewrite)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add the missing required steps",
                ct => ReplaceAsync(context.Document, rewrite.Old, rewrite.New, ct),
                "RASK095_AddRequiredSteps"),
            diagnostic);
    }

    private static List<(string Name, string Placeholder)> Parse(string steps) =>
        steps.Split(';')
            .Select(static s => s.IndexOf(':') is var i and >= 0 ? (s.Substring(0, i), s.Substring(i + 1)) : (s, ""))
            .ToList();

    // The chain from the flagged expression up to its last step, rewritten with the missing steps first.
    private static (ExpressionSyntax Old, ExpressionSyntax New)? Rewrite(
        ExpressionSyntax chain, List<(string Name, string Placeholder)> missing)
    {
        var links = new List<InvocationExpressionSyntax>();
        ExpressionSyntax top = chain;
        while (top.Parent is MemberAccessExpressionSyntax access && access.Expression == top
               && access.Parent is InvocationExpressionSyntax invocation)
        {
            links.Add(invocation);
            top = invocation;
        }

        var later = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var link in links)
        {
            var name = ((MemberAccessExpressionSyntax)link.Expression).Name.Identifier.ValueText;
            if (missing.Any(m => m.Name == name) && !later.ContainsKey(name))
            {
                later[name] = link.ArgumentList.ToString();
            }
        }

        var text = new StringBuilder(chain.WithoutTrivia().ToString());
        foreach (var (name, placeholder) in missing)
        {
            if (later.TryGetValue(name, out var arguments))
            {
                text.Append('.').Append(name).Append(arguments);
            }
            else if (placeholder.Length != 0)
            {
                text.Append('.').Append(name).Append('(').Append(placeholder).Append(')');
            }
            else
            {
                return null;
            }
        }

        // Every other link keeps its own text — the dot, its line break and indentation included.
        ExpressionSyntax receiver = chain;
        foreach (var link in links)
        {
            var name = ((MemberAccessExpressionSyntax)link.Expression).Name.Identifier.ValueText;
            if (!later.ContainsKey(name))
            {
                var full = link.ToString();
                text.Append(full, receiver.Span.End - link.Span.Start, full.Length - (receiver.Span.End - link.Span.Start));
            }
            else
            {
                later.Remove(name);
            }

            receiver = link;
        }

        var rewritten = SyntaxFactory.ParseExpression(text.ToString()).WithTriviaFrom(top);
        return (top, rewritten);
    }

    private static async Task<Document> ReplaceAsync(
        Document document, SyntaxNode oldNode, SyntaxNode newNode, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null ? document : document.WithSyntaxRoot(root.ReplaceNode(oldNode, newNode));
    }
}
