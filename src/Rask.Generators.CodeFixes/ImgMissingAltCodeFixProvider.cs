using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.CodeFixes;

// Quick-fix for RASK023 (Img is missing Alt): append `Alt: ""` so the image is treated as decorative
// and assistive tech skips it. The empty string is the safe default the analyzer message itself
// suggests — the developer replaces it with real alt text when the image is informative. Appending a
// named argument is always valid here because the analyzer fires only when no Alt was supplied.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ImgMissingAltCodeFixProvider))]
[Shared]
public sealed class ImgMissingAltCodeFixProvider : RaskCodeFixProvider<InvocationExpressionSyntax>
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("RASK023");

    protected override string Title => "Add Alt: \"\" (decorative image)";

    protected override string EquivalenceKey => "RASK023_AddEmptyAlt";

    protected override Task<Document> FixAsync(
        Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var altArgument = SyntaxFactory.Argument(
            SyntaxFactory.NameColon("Alt"),
            default,
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(string.Empty)));

        var newInvocation = invocation.WithArgumentList(invocation.ArgumentList.AddArguments(altArgument));
        return ReplaceNodeAsync(document, invocation, newInvocation, cancellationToken);
    }
}
