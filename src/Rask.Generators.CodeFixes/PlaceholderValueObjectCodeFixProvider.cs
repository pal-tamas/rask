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
///     RASK150 — a required value object's placeholder initializer becomes <c>null!</c>:
///     <c>= DriverName.Create("Unknown");</c> → <c>= null!;</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PlaceholderValueObjectCodeFixProvider))]
[Shared]
public sealed class PlaceholderValueObjectCodeFixProvider : RaskCodeFixProvider<EqualsValueClauseSyntax>
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK150"];

    protected override string Title => "Start it as null!";

    protected override string EquivalenceKey => "RASK150_NullForgiven";

    protected override Task<Document> FixAsync(Document document, EqualsValueClauseSyntax node, CancellationToken cancellationToken) =>
        ReplaceNodeAsync(
            document,
            node,
            node.WithValue(PostfixUnaryExpression(SyntaxKind.SuppressNullableWarningExpression, LiteralExpression(SyntaxKind.NullLiteralExpression))
                .WithTriviaFrom(node.Value)),
            cancellationToken);
}
