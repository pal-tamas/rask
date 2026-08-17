using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.CodeFixes;

// Quick-fix for RASK027 (both OnX and OnXAsync set on one component): drop the async one.
//
// Which of the two to drop is a real choice — but the analyzer already made it by pointing at the async
// sibling, and the title says which one goes, so the lightbulb preview shows the author exactly what they
// are agreeing to. Keeping the sync one is also the reversible direction: the async handler's body is one
// edit away from being moved into the sync one, where the reverse loses a signature.
//
// Two shapes, because RASK027 now fires on both. On a factory call the async handler is a named ARGUMENT
// and goes with the argument. On a chain it is a STEP, and removing it means splicing the chain back
// together — `Button.OnClick(a).OnClickAsync(b)` becomes `Button.OnClick(a)`.
//
// The node is taken exactly where the diagnostic points, and this provider deliberately does NOT walk up
// looking for a shape it likes: the diagnostic sits inside an expression that may itself be an argument to
// something else, and walking up from a chain once meant the fix deleted the entire enclosing argument —
// `Wrap(Content: <chain>, Label: "hi")` became `Wrap(Label: "hi")`, silently taking the component with it.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SyncAsyncHandlerCodeFixProvider))]
[Shared]
public sealed class SyncAsyncHandlerCodeFixProvider : RaskCodeFixProvider<SyntaxNode>
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK027"];

    protected override string Title => "Remove the async handler";

    protected override string EquivalenceKey => "RASK027_RemoveAsyncHandler";

    protected override Task<bool> CanFixAsync(CodeFixContext context, SyntaxNode node) =>
        Task.FromResult(AsyncArgument(node) is not null || AsyncStep(node) is not null);

    protected override async Task<Document> FixAsync(
        Document document,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        if (AsyncArgument(node) is { Parent: ArgumentListSyntax list } argument)
        {
            return await ReplaceNodeAsync(
                document,
                list,
                list.WithArguments(list.Arguments.Remove(argument)),
                cancellationToken).ConfigureAwait(false);
        }

        if (AsyncStep(node) is { } step && step.Expression is MemberAccessExpressionSyntax member)
        {
            // Splice the step out: the chain continues from whatever it was applied to.
            return await ReplaceNodeAsync(
                document,
                step,
                member.Expression.WithTriviaFrom(step),
                cancellationToken).ConfigureAwait(false);
        }

        return document;
    }

    // The factory shape: the diagnostic points at the named argument itself.
    private static ArgumentSyntax? AsyncArgument(SyntaxNode node) =>
        node is ArgumentSyntax { NameColon: not null, Parent: ArgumentListSyntax } argument ? argument : null;

    // The chain shape: the diagnostic points at the step's NAME, whose grandparent is the step call.
    private static InvocationExpressionSyntax? AsyncStep(SyntaxNode node) =>
        node is SimpleNameSyntax name
        && name.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax invocation } member
        && member.Name == name
            ? invocation
            : null;
}
