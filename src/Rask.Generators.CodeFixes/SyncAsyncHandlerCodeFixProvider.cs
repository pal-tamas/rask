using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.CodeFixes;

// Quick-fix for RASK027 (both OnX and OnXAsync passed to one factory call): drop the async one.
//
// The diagnostic is anchored on the async argument itself, so this is a single-node deletion with
// nothing to infer. Which of the two to drop is a real choice — but the analyzer already made it by
// pointing at the async sibling, and the title says which one goes, so the lightbulb preview shows the
// author exactly what they are agreeing to. Keeping the sync one is also the reversible direction: the
// async handler's body is one edit away from being moved into the sync one, where the reverse loses a
// signature.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SyncAsyncHandlerCodeFixProvider))]
[Shared]
public sealed class SyncAsyncHandlerCodeFixProvider : RaskCodeFixProvider<ArgumentSyntax>
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK027"];

    protected override string Title => "Remove the async handler argument";

    protected override string EquivalenceKey => "RASK027_RemoveAsyncArgument";

    protected override Task<bool> CanFixAsync(CodeFixContext context, ArgumentSyntax node) =>
        Task.FromResult(node is { NameColon: not null, Parent: ArgumentListSyntax });

    protected override async Task<Document> FixAsync(
        Document document,
        ArgumentSyntax node,
        CancellationToken cancellationToken)
    {
        if (node.Parent is not ArgumentListSyntax list)
        {
            return document;
        }

        return await ReplaceNodeAsync(
            document,
            list,
            list.WithArguments(list.Arguments.Remove(node)),
            cancellationToken).ConfigureAwait(false);
    }
}
