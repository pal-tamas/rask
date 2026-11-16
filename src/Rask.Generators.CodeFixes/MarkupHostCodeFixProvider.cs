using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.CodeFixes;

// Quick-fix for RASK043 and RASK118 (a chain entry named in a type that is not a markup host): mark the
// enclosing class `[Rask.Core.RaskMarkup]` and make it `partial`, along with every type it is nested in —
// the generator injects the entries through a partial of each.
//
// The attribute rather than `: RaskMarkup` because it is right for every class: when the base slot is free
// the generator writes the base itself, and when it is taken (or the class is static) it injects instead.
// Offered only for a class — a host is always one — that does not carry the attribute yet.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MarkupHostCodeFixProvider))]
[Shared]
public sealed class MarkupHostCodeFixProvider : RaskCodeFixProvider<TypeDeclarationSyntax>
{
    private const string AttributeName = "Rask.Core.RaskMarkup";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("RASK043", "RASK118");

    protected override string Title => "Make it a markup host: add [RaskMarkup] and 'partial'";

    protected override string EquivalenceKey => "RASK118_MarkupHost";

    protected override Task<bool> CanFixAsync(CodeFixContext context, TypeDeclarationSyntax node) =>
        Task.FromResult(node is ClassDeclarationSyntax && !HasMarkupAttribute(node));

    protected override async Task<Document> FixAsync(
        Document document, TypeDeclarationSyntax node, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var containers = node.Ancestors().OfType<TypeDeclarationSyntax>().ToList();
        var rewritten = root.ReplaceNodes(
            containers.Append(node),
            (original, current) => ReferenceEquals(original, node)
                ? WithMarkupAttribute(Partial(current))
                : Partial(current));

        return document.WithSyntaxRoot(rewritten);
    }

    private static bool HasMarkupAttribute(TypeDeclarationSyntax node) =>
        node.AttributeLists.SelectMany(static list => list.Attributes).Any(static attribute =>
            attribute.Name.ToString() is "RaskMarkup" or "RaskMarkupAttribute" or AttributeName
                or "Rask.Core.RaskMarkupAttribute" or "global::Rask.Core.RaskMarkup");

    // `partial` goes last among the modifiers, right before the keyword, as C# requires. With no modifiers
    // at all the keyword's leading trivia (indentation, a doc comment) moves onto it.
    private static TypeDeclarationSyntax Partial(TypeDeclarationSyntax type)
    {
        if (type.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return type;
        }

        if (type.Modifiers.Count == 0)
        {
            var partial = SyntaxFactory.Token(SyntaxKind.PartialKeyword)
                .WithLeadingTrivia(type.Keyword.LeadingTrivia)
                .WithTrailingTrivia(SyntaxFactory.Space);
            return type
                .WithKeyword(type.Keyword.WithLeadingTrivia())
                .WithModifiers(SyntaxFactory.TokenList(partial));
        }

        return type.AddModifiers(SyntaxFactory.Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(SyntaxFactory.Space));
    }

    // The attribute takes the declaration's leading trivia (its indentation and any doc comment) and the
    // line after it is indented to match.
    private static TypeDeclarationSyntax WithMarkupAttribute(TypeDeclarationSyntax type)
    {
        var leading = type.GetLeadingTrivia();
        var indentation = leading.LastOrDefault(static t => t.IsKind(SyntaxKind.WhitespaceTrivia));
        var attribute = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(SyntaxFactory.ParseName(AttributeName))))
            .WithLeadingTrivia(leading)
            .WithTrailingTrivia(SyntaxFactory.EndOfLine("\n"));

        var bare = type.WithLeadingTrivia(indentation.IsKind(SyntaxKind.WhitespaceTrivia)
            ? SyntaxFactory.TriviaList(indentation)
            : SyntaxFactory.TriviaList());
        return bare.WithAttributeLists(bare.AttributeLists.Insert(0, attribute));
    }
}
