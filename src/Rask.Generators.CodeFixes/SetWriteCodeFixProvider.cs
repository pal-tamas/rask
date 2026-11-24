using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Rask.Generators.CodeFixes;

/// <summary>
///     RASK187 — a raw set write becomes the aggregate's own:
///     <c>await db.Pairings.Where(p =&gt; p.Pump == id).ExecuteDeleteAsync(token)</c> →
///     <c>await Pairing.DeleteWhere(p =&gt; p.Pump == id, token)</c>, and <c>ExecuteUpdateAsync(s =&gt; …)</c> →
///     <c>Pairing.UpdateWhere(p =&gt; …, s =&gt; …)</c>.
/// </summary>
/// <remarks>
///     The analyzer (RawSetWriteAnalyzer) offers it only for the awaitable form on a set, straight or behind one
///     <c>Where</c>, and hands over the predicate, the setters and the token as additional locations; a set written
///     straight is every row, <c>_ =&gt; true</c>.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SetWriteCodeFixProvider))]
[Shared]
public sealed class SetWriteCodeFixProvider : CodeFixProvider
{
    // The analyzer's property names, restated: this assembly does not reference it.
    private const string WriteProperty = "Write";
    private const string AggregateProperty = "Aggregate";
    private const string WhereProperty = "Where";
    private const string TokenProperty = "Token";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK187"];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null ||
            !diagnostic.Properties.TryGetValue(WriteProperty, out var write) || write is null ||
            !diagnostic.Properties.TryGetValue(AggregateProperty, out var aggregate) || aggregate is null ||
            root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<InvocationExpressionSyntax>() is not { } call)
        {
            return;
        }

        var hasWhere = diagnostic.Properties.ContainsKey(WhereProperty);
        var hasToken = diagnostic.Properties.ContainsKey(TokenProperty);
        var locations = diagnostic.AdditionalLocations;
        var arguments = new List<ArgumentSyntax>();
        foreach (var location in locations)
        {
            if (Expression(root, location) is not { } argument)
            {
                return;
            }

            arguments.Add(Argument(argument.WithoutTrivia()));
        }

        if (!hasWhere)
        {
            arguments.Insert(0, Argument(SimpleLambdaExpression(Parameter(Identifier("_")), LiteralExpression(SyntaxKind.TrueLiteralExpression))));
        }

        if (hasToken)
        {
            arguments[arguments.Count - 1] = arguments[arguments.Count - 1].WithNameColon(NameColon("cancellationToken"));
        }

        var replacement = InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ParseExpression(aggregate), IdentifierName(write)),
                ArgumentList(SeparatedList(arguments)))
            .WithTriviaFrom(call);
        context.RegisterCodeFix(
            CodeAction.Create(
                "Call '" + aggregate + "." + write + "'",
                _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(call, replacement))),
                "RASK187_SetWrite"),
            diagnostic);
    }

    private static ExpressionSyntax? Expression(SyntaxNode root, Location location) =>
        root.FindNode(location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<ExpressionSyntax>();
}
