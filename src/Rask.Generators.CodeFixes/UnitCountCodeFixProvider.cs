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

// Quick-fix for RASK092 (unit reads wrong for its count): `2.Hour` → `2.Hours`, `1.Hours` → `1.Hour`. The
// analyzer has already worked out the other form and hands it over in the diagnostic's properties, so the
// two can never disagree about which unit pairs with which.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UnitCountCodeFixProvider))]
[Shared]
public sealed class UnitCountCodeFixProvider : CodeFixProvider
{
    private const string ReplacementKey = "Replacement";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("RASK092");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        if (root?.FindNode(diagnostic.Location.SourceSpan) is not SimpleNameSyntax name
            || !diagnostic.Properties.TryGetValue(ReplacementKey, out var replacement)
            || replacement is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Write '{replacement}'",
                ct => SwapAsync(context.Document, name, replacement, ct),
                "RASK092_" + replacement),
            diagnostic);
    }

    private static async Task<Document> SwapAsync(
        Document document, SimpleNameSyntax name, string replacement, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var swapped = SyntaxFactory.IdentifierName(replacement).WithTriviaFrom(name);
        return root is null ? document : document.WithSyntaxRoot(root.ReplaceNode(name, swapped));
    }
}
