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
///     RASK129 — a record declared from a base record gains the <c>sealed</c> and <c>partial</c> it lacks:
///     <c>public record DriverName() : Text(…)</c> becomes <c>public sealed partial record DriverName() : Text(…)</c>.
/// </summary>
/// <remarks>Withheld when the record already has both, and only a type it is nested in is not partial.</remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ValueObjectModifiersCodeFixProvider))]
[Shared]
public sealed class ValueObjectModifiersCodeFixProvider : RaskCodeFixProvider<RecordDeclarationSyntax>
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK129"];

    protected override string Title => "Make it sealed partial";

    protected override string EquivalenceKey => "RASK129_SealedPartial";

    protected override Task<bool> CanFixAsync(CodeFixContext context, RecordDeclarationSyntax node) =>
        Task.FromResult(!node.Modifiers.Any(SyntaxKind.SealedKeyword) || !node.Modifiers.Any(SyntaxKind.PartialKeyword));

    protected override Task<Document> FixAsync(Document document, RecordDeclarationSyntax node, CancellationToken cancellationToken) =>
        ReplaceNodeAsync(document, node, WithModifiers(node), cancellationToken);

    // `sealed` goes before `partial`, and `partial` last, right before `record`, where C# requires it.
    private static RecordDeclarationSyntax WithModifiers(RecordDeclarationSyntax node)
    {
        var modifiers = node.Modifiers;
        var leading = modifiers.Count > 0 ? modifiers[0].LeadingTrivia : node.Keyword.LeadingTrivia;
        var partialAt = modifiers.IndexOf(SyntaxKind.PartialKeyword);

        if (!modifiers.Any(SyntaxKind.SealedKeyword))
        {
            var sealedToken = Token(SyntaxKind.SealedKeyword).WithTrailingTrivia(Space);
            modifiers = partialAt >= 0 ? modifiers.Insert(partialAt, sealedToken) : modifiers.Add(sealedToken);
        }

        if (!modifiers.Any(SyntaxKind.PartialKeyword))
        {
            modifiers = modifiers.Add(Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(Space));
        }

        modifiers = modifiers.Replace(modifiers[0], modifiers[0].WithLeadingTrivia(leading));
        return node.WithModifiers(modifiers).WithKeyword(node.Keyword.WithLeadingTrivia());
    }
}
