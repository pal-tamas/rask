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
        // Only the call this node is the CALLEE of — never merely an enclosing one. Walking up to the
        // nearest ancestor invocation reached straight out of the Img being fixed: `Wrap(Img)` became
        // `Wrap(Img).Alt("")`, which neither compiles nor gives the image a text alternative, and
        // `Generated.Div(Children: [Img])` had `Alt: ""` appended to the *Div*.
        var enclosing = CalleeOf(node);

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

    // The invocation this node is the callee of — `Img(…)`, or the `Img` in `Generated.Img(…)`. Null when
    // the node merely sits inside some other call, which is the case that used to be mishandled.
    private static InvocationExpressionSyntax? CalleeOf(ExpressionSyntax node) => node.Parent switch
    {
        InvocationExpressionSyntax invocation when invocation.Expression == node => invocation,
        MemberAccessExpressionSyntax member when member.Name == node
                                                 && member.Parent is InvocationExpressionSyntax outer
                                                 && outer.Expression == member => outer,
        _ => null,
    };

    // The end of the chain, so the new step lands after every existing one. Climbs only while the node is
    // the RECEIVER of the next step, so it can never walk out into an enclosing expression.
    private static ExpressionSyntax Outermost(ExpressionSyntax node)
    {
        var current = node;
        while (current.Parent is MemberAccessExpressionSyntax member
               && member.Expression == current
               && member.Parent is InvocationExpressionSyntax outer)
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
