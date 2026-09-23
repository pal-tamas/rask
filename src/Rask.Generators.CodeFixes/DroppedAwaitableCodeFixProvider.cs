using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.CodeFixes;

// Quick-fixes for RASK093 (an awaitable dropped in a method that is not async). Two fixes, because there
// are two honest intentions: you meant to wait for it, or you meant to let it run unwatched. Offering
// only the first would make the second look like a mistake it is not — `_ = Task.Run(…)` is how C# says
// "on purpose", and the analyzer exists to make that choice visible rather than to forbid one side of it.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DroppedAwaitableCodeFixProvider))]
[Shared]
public sealed class DroppedAwaitableCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("RASK093");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        if (root?.FindNode(diagnostic.Location.SourceSpan) is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create("Await it", ct => AwaitAsync(context.Document, invocation, ct), "RASK093_await"),
            diagnostic);

        context.RegisterCodeFix(
            CodeAction.Create(
                "Discard it — let it run unwatched",
                ct => DiscardAsync(context.Document, invocation, ct),
                "RASK093_discard"),
            diagnostic);
    }

    /// <summary>
    /// Wraps the call in <c>await</c> and makes the enclosing method async, giving it a <c>Task</c>
    /// return type when it returned <c>void</c> — which is what awaiting inside it requires.
    /// </summary>
    private static async Task<Document> AwaitAsync(
        Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var awaited = SyntaxFactory.AwaitExpression(invocation.WithoutTrivia()).WithTriviaFrom(invocation);

        // One replacement, not two. The method CONTAINS the invocation, so replacing both against the
        // original tree makes the outer edit rebuild from the unmodified method and drop the await — a
        // fix that reports success and changes nothing.
        if (Enclosing(invocation) is { } method && !method.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            return document.WithSyntaxRoot(
                root.ReplaceNode(method, Asynchronous(method.ReplaceNode(invocation, awaited))));
        }

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, awaited));
    }

    /// <summary>Assigns it to a discard, the C# spelling of "unwatched, on purpose".</summary>
    private static async Task<Document> DiscardAsync(
        Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var discarded = SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName("_"),
                invocation.WithoutTrivia())
            .WithTriviaFrom(invocation);

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, discarded));
    }

    private static MethodDeclarationSyntax? Enclosing(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case MethodDeclarationSyntax method:
                    return method;

                // A lambda or local function is its own scope; fixing the method around it would be wrong.
                case AnonymousFunctionExpressionSyntax:
                case LocalFunctionStatementSyntax:
                    return null;
            }
        }

        return null;
    }

    private static MethodDeclarationSyntax Asynchronous(MethodDeclarationSyntax method)
    {
        var withAsync = method.AddModifiers(SyntaxFactory.Token(SyntaxKind.AsyncKeyword));

        // `async void` is only ever right for an event handler, and an unobserved exception in one takes
        // the process down. A method that returned void returns Task instead.
        return method.ReturnType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword }
            ? withAsync.WithReturnType(
                SyntaxFactory.IdentifierName("Task").WithTriviaFrom(method.ReturnType))
            : withAsync;
    }
}
