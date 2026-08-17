using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.CodeFixes;

// Quick-fix for RASK023 (Img is missing Alt): give the image an empty Alt so it reads as decorative
// and assistive tech skips it. The empty string is the safe default the analyzer message itself
// suggests — the developer replaces it with real alt text when the image is informative.
//
// Two shapes, because RASK023 now fires on both. A chain gets a `.Alt("")` step appended to its END,
// which is where a step belongs and is valid wherever the chain already was — including on a bare
// entry (`Img` becomes `Img.Alt("")`), which carries no argument list to add anything to. The older
// generated factory call keeps taking a named `Alt: ""` argument. Appending is always valid here
// because the analyzer fires only when no Alt was supplied at all.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ImgMissingAltCodeFixProvider))]
[Shared]
public sealed class ImgMissingAltCodeFixProvider : RaskCodeFixProvider<ExpressionSyntax>
{
    private const string GeneratedClassName = "Generated";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("RASK023");

    protected override string Title => "Add an empty Alt (decorative image)";

    protected override string EquivalenceKey => "RASK023_AddEmptyAlt";

    protected override async Task<Document> FixAsync(
        Document document, ExpressionSyntax node, CancellationToken cancellationToken)
    {
        var enclosing = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

        if (enclosing is not null && await IsFactoryCallAsync(document, enclosing, cancellationToken)
                .ConfigureAwait(false))
        {
            var argument = SyntaxFactory.Argument(
                SyntaxFactory.NameColon("Alt"),
                default,
                EmptyString());

            return await ReplaceNodeAsync(
                document,
                enclosing,
                enclosing.WithArgumentList(enclosing.ArgumentList.AddArguments(argument)),
                cancellationToken).ConfigureAwait(false);
        }

        // A chain: append `.Alt("")` to the whole thing, not to whichever step happens to be nearest.
        var chain = Outermost(enclosing ?? node);
        var stepped = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                chain.WithoutTrivia(),
                SyntaxFactory.IdentifierName("Alt")),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(EmptyString()))));

        return await ReplaceNodeAsync(document, chain, stepped.WithTriviaFrom(chain), cancellationToken)
            .ConfigureAwait(false);
    }

    private static LiteralExpressionSyntax EmptyString() =>
        SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(string.Empty));

    // The end of the chain, so the new step lands after every existing one.
    private static ExpressionSyntax Outermost(ExpressionSyntax node)
    {
        var current = node;
        while (current.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax outer })
        {
            current = outer;
        }

        return current;
    }

    private static async Task<bool> IsFactoryCallAsync(
        Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        return model?.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol { IsStatic: true } method
               && method.ContainingType?.Name == GeneratedClassName;
    }
}
