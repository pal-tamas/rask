using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Rask.Generators.CodeFixes;

/// <summary>
///     RASK130 — <c>[StringLength(200)] public string Name { get; private set; }</c>, or one ruled
///     <c>rules.RuleFor(s =&gt; s.Name).NotEmpty().MaximumLength(200)</c> in its type's <c>Rules</c>, becomes
///     <c>public SupplierName Name { get; private set; }</c> beside
///     <c>public sealed partial record SupplierName(string Value) : Text(Value, MaxLength: 200);</c>.
/// </summary>
/// <remarks>
///     The value object is named after its type and the property, declared from the base record for what the property
///     holds (<c>Text</c>, <c>Number</c>, <c>Integer</c>, <c>Day</c>), and given the arguments its rules say; each rule a
///     base argument now carries is taken off the property. A FluentValidation check no argument says (<c>Matches</c>,
///     <c>Must</c>) is left where it is, for the author to move into the value object. What is not offered, and why, is
///     <see cref="ValueObjectDraftReader" />'s to say.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ValueObjectFromRulesCodeFixProvider))]
[Shared]
public sealed class ValueObjectFromRulesCodeFixProvider : RaskCodeFixProvider<PropertyDeclarationSyntax>
{
    private const string ValuesNamespace = "Rask.Values";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK130"];

    protected override string Title => "Declare a value object from its rules";

    protected override string EquivalenceKey => "RASK130_DeclareValueObject";

    protected override async Task<bool> CanFixAsync(CodeFixContext context, PropertyDeclarationSyntax node) =>
        await DraftAsync(context.Document, node, context.CancellationToken).ConfigureAwait(false) is not null;

    protected override async Task<Document> FixAsync(Document document, PropertyDeclarationSyntax node, CancellationToken cancellationToken)
    {
        if (await DraftAsync(document, node, cancellationToken).ConfigureAwait(false) is not { } draft ||
            node.Ancestors().OfType<BaseTypeDeclarationSyntax>().LastOrDefault() is not { } outermost)
        {
            return document;
        }

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        foreach (var chain in draft.Chains)
        {
            Rewrite(editor, chain);
        }

        var newLine = NewLine(await outermost.SyntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false));
        editor.ReplaceNode(node, Retyped(node, draft));
        editor.InsertAfter(outermost, Record(draft, outermost, newLine));

        var changed = editor.GetChangedRoot();
        if (draft.NeedsUsing && changed is CompilationUnitSyntax unit)
        {
            changed = UsingDirectives.With(unit, ValuesNamespace);
        }

        return document.WithSyntaxRoot(changed);
    }

    private static async Task<ValueObjectDraft?> DraftAsync(Document document, PropertyDeclarationSyntax node, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        return model is null ? null : ValueObjectDraftReader.Read(model, node, cancellationToken);
    }

    // The rule without the checks the value object now makes; gone, when it made nothing else.
    private static void Rewrite(DocumentEditor editor, FluentChainEdit chain)
    {
        if (!chain.RemovesTheRule)
        {
            editor.ReplaceNode(chain.Outermost, Rebuilt(chain).WithTriviaFrom(chain.Outermost));
            return;
        }

        switch (chain.Outermost.Parent)
        {
            case ExpressionStatementSyntax statement:
                editor.RemoveNode(statement, SyntaxRemoveOptions.KeepNoTrivia);
                break;
            case ArrowExpressionClauseSyntax { Parent: MethodDeclarationSyntax method }:
                editor.ReplaceNode(method, method
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithBody(Block(Token(SyntaxKind.OpenBraceToken).WithTrailingTrivia(Space), default, Token(SyntaxKind.CloseBraceToken))
                        .WithTrailingTrivia(method.SemicolonToken.TrailingTrivia)));
                break;
            default:
                editor.ReplaceNode(chain.Outermost, chain.RuleFor.WithTriviaFrom(chain.Outermost));
                break;
        }
    }

    // `rules.RuleFor(s => s.Name)` and the calls kept on it, each with the trivia it was written with.
    private static ExpressionSyntax Rebuilt(FluentChainEdit chain)
    {
        ExpressionSyntax current = chain.RuleFor;
        foreach (var kept in chain.Kept)
        {
            var access = (MemberAccessExpressionSyntax)kept.Expression;
            current = kept.WithExpression(access.WithExpression(current));
        }

        return current;
    }

    // The property typed as the value object, without the attributes it carries now, and initialized as one.
    private static PropertyDeclarationSyntax Retyped(PropertyDeclarationSyntax node, ValueObjectDraft draft)
    {
        var lists = node.AttributeLists
            .Select(list => list.WithAttributes(SeparatedList(list.Attributes.Where(a => !draft.RedundantAttributes.Contains(a)))))
            .Where(static list => list.Attributes.Count > 0);
        TypeSyntax type = draft.Nullable ? NullableType(IdentifierName(draft.Name)) : IdentifierName(draft.Name);
        var retyped = node
            .WithAttributeLists(List(lists))
            .WithType(type.WithTriviaFrom(node.Type))
            .WithLeadingTrivia(node.GetLeadingTrivia());

        if (draft.Nullable)
        {
            // Null until given: no initializer, and the line ends where the accessors do.
            return retyped.Initializer is null || retyped.AccessorList is null
                ? retyped
                : retyped
                    .WithAccessorList(retyped.AccessorList.WithTrailingTrivia(retyped.SemicolonToken.TrailingTrivia))
                    .WithInitializer(null)
                    .WithSemicolonToken(default);
        }

        var notYet = PostfixUnaryExpression(SyntaxKind.SuppressNullableWarningExpression, LiteralExpression(SyntaxKind.NullLiteralExpression));
        if (retyped.Initializer is { } initializer)
        {
            return retyped.WithInitializer(initializer.WithValue(notYet.WithTriviaFrom(initializer.Value)));
        }

        if (retyped.AccessorList is not { } accessors)
        {
            return retyped;
        }

        // `{ get; private set; }` becomes `{ get; private set; } = null!;`, the line still ending after it.
        return retyped
            .WithAccessorList(accessors.WithoutTrailingTrivia())
            .WithInitializer(EqualsValueClause(notYet.WithLeadingTrivia(Space)).WithLeadingTrivia(Space))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken).WithTrailingTrivia(accessors.GetTrailingTrivia()));
    }

    // The value object's declaration, a line after the type the property is in, indented as it is.
    private static MemberDeclarationSyntax Record(ValueObjectDraft draft, BaseTypeDeclarationSyntax outermost, SyntaxTrivia newLine)
    {
        var indentation = outermost.GetLeadingTrivia().LastOrDefault(static t => t.IsKind(SyntaxKind.WhitespaceTrivia));
        var leading = indentation == default ? TriviaList(newLine) : TriviaList(newLine, indentation);
        return ParseMemberDeclaration(draft.Declaration)!
            .WithLeadingTrivia(leading)
            .WithTrailingTrivia(newLine);
    }

    // The file's own line ending: the first one it uses, or a line feed.
    private static SyntaxTrivia NewLine(SyntaxNode root) =>
        root.DescendantTrivia().FirstOrDefault(static t => t.IsKind(SyntaxKind.EndOfLineTrivia)) is { } found && found != default
            ? found
            : LineFeed;
}
