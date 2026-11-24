using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Rask.Generators.CodeFixes;

/// <summary>
///     RASK186 — an update that only runs an operation becomes the operation's generated entry:
///     <c>await Supply.Update(id, s =&gt; s.Change(details), version: v)</c> → <c>await Supply.Change(id, v, details)</c>.
/// </summary>
/// <remarks>
///     The analyzer (UpdateThroughOperationAnalyzer) proved the entry exists and takes these arguments, and hands over
///     the id, the operation call, the version and the token as additional locations; the token is passed by name.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OperationEntryCodeFixProvider))]
[Shared]
public sealed class OperationEntryCodeFixProvider : CodeFixProvider
{
    // The analyzer's property names, restated: this assembly does not reference it.
    private const string OperationProperty = "Operation";
    private const string VersionProperty = "Version";
    private const string TokenProperty = "Token";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK186"];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || !diagnostic.Properties.TryGetValue(OperationProperty, out var operation) || operation is null ||
            root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<InvocationExpressionSyntax>() is not
            { Expression: MemberAccessExpressionSyntax update } call)
        {
            return;
        }

        var hasVersion = diagnostic.Properties.ContainsKey(VersionProperty);
        var hasToken = diagnostic.Properties.ContainsKey(TokenProperty);
        var locations = diagnostic.AdditionalLocations;
        if (locations.Count != 2 + (hasVersion ? 1 : 0) + (hasToken ? 1 : 0) ||
            Expression(root, locations[0]) is not { } id ||
            Expression(root, locations[1]) is not InvocationExpressionSyntax inner)
        {
            return;
        }

        var arguments = new List<ArgumentSyntax> { Argument(id.WithoutTrivia()) };
        if (hasVersion && Expression(root, locations[2]) is { } version)
        {
            arguments.Add(Argument(version.WithoutTrivia()));
        }

        arguments.AddRange(inner.ArgumentList.Arguments);
        if (hasToken && Expression(root, locations[locations.Count - 1]) is { } token)
        {
            arguments.Add(Argument(NameColon("cancellationToken"), default, token.WithoutTrivia()));
        }

        var entry = InvocationExpression(update.WithName(IdentifierName(operation)), ArgumentList(SeparatedList(arguments)))
            .WithTriviaFrom(call);
        context.RegisterCodeFix(
            CodeAction.Create(
                "Call the generated '" + operation + "' entry",
                _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(call, entry))),
                "RASK186_CallEntry"),
            diagnostic);
    }

    private static ExpressionSyntax? Expression(SyntaxNode root, Location location) =>
        root.FindNode(location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<ExpressionSyntax>();
}
