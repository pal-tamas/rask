using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Rask.Generators.CodeFixes;

/// <summary>
///     RASK185 — the load moves into the transaction:
///     <c>var supply = await Supply.Find(id); await Db.Transaction(async () =&gt; { … });</c> →
///     <c>await Db.Transaction(async () =&gt; { var supply = await Supply.Find(id); … });</c>.
/// </summary>
/// <remarks>
///     Offered only where the analyzer found the move safe (its <c>Movable</c> property): the declaration declares the
///     one local, sits before the transaction in the same block, and nothing outside the work reads it. An expression
///     body becomes a block — a <c>return</c> when the work returns a value.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LoadInsideTransactionCodeFixProvider))]
[Shared]
public sealed class LoadInsideTransactionCodeFixProvider : CodeFixProvider
{
    // The analyzer's property names (OutsideTransactionLoadAnalyzer), restated: this assembly does not reference it.
    private const string MovableProperty = "Movable";
    private const string ReturnsProperty = "Returns";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK185"];

    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        if (!diagnostic.Properties.ContainsKey(MovableProperty) || diagnostic.AdditionalLocations.Count != 2)
        {
            return;
        }

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(diagnostic.AdditionalLocations[0].SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<LocalDeclarationStatementSyntax>() is not { } declaration ||
            root.FindNode(diagnostic.AdditionalLocations[1].SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>() is not { } work)
        {
            return;
        }

        var returns = diagnostic.Properties.ContainsKey(ReturnsProperty);
        context.RegisterCodeFix(
            CodeAction.Create(
                "Load it inside the transaction",
                _ => Task.FromResult(context.Document.WithSyntaxRoot(Moved(root, declaration, work, returns))),
                "RASK185_LoadInside"),
            diagnostic);
    }

    private static SyntaxNode Moved(SyntaxNode root, LocalDeclarationStatementSyntax declaration, AnonymousFunctionExpressionSyntax work, bool returns)
    {
        var moved = declaration.WithoutLeadingTrivia().WithAdditionalAnnotations(Formatter.Annotation);
        var body = work.Body switch
        {
            BlockSyntax block => block.WithStatements(block.Statements.Insert(0, moved)),
            ExpressionSyntax expression => Block(
                moved,
                returns ? ReturnStatement(expression.WithoutTrivia()) : ExpressionStatement(expression.WithoutTrivia())),
            _ => null,
        };
        if (body is null)
        {
            return root;
        }

        var rewritten = work switch
        {
            ParenthesizedLambdaExpressionSyntax lambda => (SyntaxNode)lambda.WithExpressionBody(null).WithBlock(body.WithAdditionalAnnotations(Formatter.Annotation)),
            SimpleLambdaExpressionSyntax lambda => lambda.WithExpressionBody(null).WithBlock(body.WithAdditionalAnnotations(Formatter.Annotation)),
            AnonymousMethodExpressionSyntax method => method.WithBlock(body),
            _ => work,
        };

        var tracked = root.TrackNodes(declaration, work);
        tracked = tracked.ReplaceNode(tracked.GetCurrentNode(work)!, rewritten);
        return tracked.RemoveNode(tracked.GetCurrentNode(declaration)!, SyntaxRemoveOptions.KeepUnbalancedDirectives) ?? root;
    }
}
