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

// Quick-fix for RASK084 (an entity's or value object's state can be changed from outside it): narrow the
// member to private — `set` becomes `private set`, `init` becomes `private init`, and a public mutable
// field becomes a private one. The message says exactly this, so the edit is mechanical.
//
// The analyzer, which has the symbols, marks the cases where `private` would not compile (a required or
// abstract property, an accessor whose sibling already carries a modifier, a setter an interface demands,
// a positional record struct parameter) with RaskNoFix, and the fix is withheld for them: the warning stands
// with its message, and the author decides.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ModelStateMutationCodeFixProvider))]
[Shared]
public sealed class ModelStateMutationCodeFixProvider : RaskCodeFixProvider<CSharpSyntaxNode>
{
    // Mirrors ModelTypes.NoFixProperty in Rask.Batteries.Generators, which this assembly cannot reference.
    private const string NoFixProperty = "RaskNoFix";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK084"];

    protected override string Title => "Make it private";

    protected override string EquivalenceKey => "RASK084_MakePrivate";

    protected override Task<bool> CanFixAsync(CodeFixContext context, CSharpSyntaxNode node) =>
        Task.FromResult(
            !context.Diagnostics[0].Properties.ContainsKey(NoFixProperty)
            && (node is AccessorDeclarationSyntax or VariableDeclaratorSyntax));

    protected override Task<Document> FixAsync(Document document, CSharpSyntaxNode node, CancellationToken cancellationToken) =>
        node switch
        {
            AccessorDeclarationSyntax accessor => ReplaceNodeAsync(document, accessor, Narrow(accessor), cancellationToken),
            VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax field } =>
                ReplaceNodeAsync(document, field, Narrow(field), cancellationToken),
            _ => Task.FromResult(document),
        };

    // `set;` -> `private set;`. The keyword's leading trivia (the space after `get;`) moves onto the new
    // modifier so the accessor keeps its place on the line.
    private static AccessorDeclarationSyntax Narrow(AccessorDeclarationSyntax accessor)
    {
        var first = accessor.Modifiers.Count > 0 ? accessor.Modifiers[0] : accessor.Keyword;
        var modifier = SyntaxFactory.Token(SyntaxKind.PrivateKeyword)
            .WithLeadingTrivia(first.LeadingTrivia)
            .WithTrailingTrivia(SyntaxFactory.Space);

        return accessor
            .WithModifiers(SyntaxFactory.TokenList(modifier))
            .WithKeyword(accessor.Keyword.WithLeadingTrivia());
    }

    // `public int Count;` -> `private int Count;`, the public token swapped in place with its trivia.
    private static FieldDeclarationSyntax Narrow(FieldDeclarationSyntax field)
    {
        var modifiers = field.Modifiers;
        var index = modifiers.IndexOf(SyntaxKind.PublicKeyword);
        if (index < 0)
        {
            return field;
        }

        var replacement = SyntaxFactory.Token(SyntaxKind.PrivateKeyword).WithTriviaFrom(modifiers[index]);
        return field.WithModifiers(modifiers.Replace(modifiers[index], replacement));
    }
}
