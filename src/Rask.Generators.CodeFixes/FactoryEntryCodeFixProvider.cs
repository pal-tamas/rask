using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Rask.Generators.CodeFixes;

/// <summary>
///     RASK133 — an aggregate built by its factory, added to a context and saved there becomes the factory's generated
///     entry: <c>await using var db = …; db.Add(Product.Place(name, price)); await db.SaveChangesAsync();</c> →
///     <c>await Product.Place(name, price, cancellationToken: default);</c>.
/// </summary>
/// <remarks>
///     The analyzer (ContextWriteAnalyzer) offers it only when the context is a local the method uses for nothing but
///     that write and that save, and hands over the factory call, the save statement, the context's declaration and the
///     save's token as additional locations. The token is passed by name, which is what binds the call to the entry
///     rather than to the factory the call site can see.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FactoryEntryCodeFixProvider))]
[Shared]
public sealed class FactoryEntryCodeFixProvider : CodeFixProvider
{
    // The analyzer's property names, restated: this assembly does not reference it.
    private const string FactoryProperty = "Factory";
    private const string TokenProperty = "Token";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK133"];

    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var hasToken = diagnostic.Properties.ContainsKey(TokenProperty);
        var locations = diagnostic.AdditionalLocations;
        if (root is null || !diagnostic.Properties.TryGetValue(FactoryProperty, out var factoryName) || factoryName is null ||
            locations.Count != 3 + (hasToken ? 1 : 0) ||
            root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<ExpressionStatementSyntax>() is not { } write ||
            root.FindNode(locations[0].SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<InvocationExpressionSyntax>() is not { } factory ||
            root.FindNode(locations[1].SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<ExpressionStatementSyntax>() is not { } save ||
            root.FindNode(locations[2].SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<LocalDeclarationStatementSyntax>() is not { } declaration)
        {
            return;
        }

        var token = hasToken
            ? root.FindNode(locations[3].SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<ExpressionSyntax>()?.WithoutTrivia()
            : null;
        var entry = ExpressionStatement(AwaitExpression(factory.WithoutTrivia().AddArgumentListArguments(
                Argument(NameColon("cancellationToken"), default, token ?? LiteralExpression(SyntaxKind.DefaultLiteralExpression)))))
            .WithTriviaFrom(write);

        context.RegisterCodeFix(
            CodeAction.Create(
                "Call the generated '" + factoryName + "' entry",
                cancellation => Rewrite(context.Document, write, entry, save, declaration, cancellation),
                "RASK133_CallFactoryEntry"),
            diagnostic);
    }

    private static async Task<Document> Rewrite(
        Document document, StatementSyntax write, StatementSyntax entry, StatementSyntax save, StatementSyntax declaration,
        CancellationToken cancellation)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellation).ConfigureAwait(false);
        editor.ReplaceNode(write, entry);
        editor.RemoveNode(save, SyntaxRemoveOptions.KeepNoTrivia);
        editor.RemoveNode(declaration, SyntaxRemoveOptions.KeepNoTrivia);
        return editor.GetChangedDocument();
    }
}
