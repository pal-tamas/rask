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

// Quick-fix for RASK104 (a static member keeps a component built from a chain entry): make it an
// expression-bodied property, so every read builds its own entry in the render doing the reading: a
// `static readonly` field initialised with `= Div` becomes a property with `=> Div`, and a get-only
// auto-property's `{ get; } = Div` becomes `=> Div`.
//
// Offered only where that means the same thing to every reader: a `readonly` field declaring ONE variable,
// or a get-only auto-property. A writable one is assigned somewhere, and a property has no setter to keep it.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StaticChainEntryCodeFixProvider))]
[Shared]
public sealed class StaticChainEntryCodeFixProvider : RaskCodeFixProvider<MemberDeclarationSyntax>
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("RASK104");

    protected override string Title => "Build it per read: make it an expression-bodied property";

    protected override string EquivalenceKey => "RASK104_ExpressionBodiedProperty";

    protected override Task<bool> CanFixAsync(CodeFixContext context, MemberDeclarationSyntax node) =>
        Task.FromResult(node switch
        {
            FieldDeclarationSyntax field => field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword)
                                            && field.Declaration.Variables.Count == 1,
            PropertyDeclarationSyntax property => IsGetOnly(property),
            _ => false,
        });

    protected override Task<Document> FixAsync(
        Document document, MemberDeclarationSyntax node, CancellationToken cancellationToken) =>
        node switch
        {
            FieldDeclarationSyntax field => ReplaceNodeAsync(document, field, FromField(field), cancellationToken),
            PropertyDeclarationSyntax property =>
                ReplaceNodeAsync(document, property, FromProperty(property), cancellationToken),
            _ => Task.FromResult(document),
        };

    private static PropertyDeclarationSyntax FromField(FieldDeclarationSyntax field)
    {
        var variable = field.Declaration.Variables[0];
        var modifiers = SyntaxFactory.TokenList(
            field.Modifiers.Where(m => !m.IsKind(SyntaxKind.ReadOnlyKeyword)));

        return SyntaxFactory.PropertyDeclaration(field.Declaration.Type, variable.Identifier.WithoutTrivia())
            .WithAttributeLists(field.AttributeLists)
            .WithModifiers(modifiers)
            .WithExpressionBody(ArrowTo(variable.Initializer!.Value))
            .WithSemicolonToken(field.SemicolonToken)
            .WithTriviaFrom(field);
    }

    private static PropertyDeclarationSyntax FromProperty(PropertyDeclarationSyntax property) =>
        property
            .WithIdentifier(property.Identifier.WithoutTrivia())
            .WithAccessorList(null)
            .WithInitializer(null)
            .WithExpressionBody(ArrowTo(property.Initializer!.Value))
            .WithSemicolonToken(property.SemicolonToken)
            .WithTriviaFrom(property);

    private static ArrowExpressionClauseSyntax ArrowTo(ExpressionSyntax value) =>
        SyntaxFactory.ArrowExpressionClause(
            SyntaxFactory.Token(SyntaxKind.EqualsGreaterThanToken)
                .WithLeadingTrivia(SyntaxFactory.Space)
                .WithTrailingTrivia(SyntaxFactory.Space),
            value.WithoutTrivia());

    private static bool IsGetOnly(PropertyDeclarationSyntax property) =>
        property.AccessorList is { Accessors.Count: 1 } list
        && list.Accessors[0] is { Body: null, ExpressionBody: null } accessor
        && accessor.IsKind(SyntaxKind.GetAccessorDeclaration);
}
