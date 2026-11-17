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

// Quick-fix for RASK121 (a [JSInvokable] method the browser cannot reach): make it public. The accessibility
// keywords (private, protected, internal, in any combination) give way to one `public` in the first one's place,
// keeping its trivia; a method with none gets `public` in front of its other modifiers.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NonPublicJSInvokableCodeFixProvider))]
[Shared]
public sealed class NonPublicJSInvokableCodeFixProvider : RaskCodeFixProvider<MethodDeclarationSyntax>
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK121"];

    protected override string Title => "Make the [JSInvokable] method public";

    protected override string EquivalenceKey => "RASK121_MakePublic";

    protected override Task<Document> FixAsync(
        Document document,
        MethodDeclarationSyntax node,
        CancellationToken cancellationToken) =>
        ReplaceNodeAsync(document, node, MakePublic(node), cancellationToken);

    private static MethodDeclarationSyntax MakePublic(MethodDeclarationSyntax method)
    {
        var access = method.Modifiers.Where(IsAccessibility).ToList();
        if (access.Count == 0)
        {
            var leading = method.Modifiers.Count > 0 ? method.Modifiers[0].LeadingTrivia : method.ReturnType.GetLeadingTrivia();
            var stripped = method.Modifiers.Count > 0
                ? method.WithModifiers(method.Modifiers.Replace(method.Modifiers[0], method.Modifiers[0].WithLeadingTrivia()))
                : method.WithReturnType(method.ReturnType.WithLeadingTrivia());
            var keyword = SyntaxFactory.Token(SyntaxKind.PublicKeyword).WithLeadingTrivia(leading)
                .WithTrailingTrivia(SyntaxFactory.Space);
            return stripped.WithModifiers(stripped.Modifiers.Insert(0, keyword));
        }

        var modifiers = method.Modifiers.Replace(
            access[0], SyntaxFactory.Token(SyntaxKind.PublicKeyword).WithTriviaFrom(access[0]));
        foreach (var extra in access.Skip(1))
        {
            modifiers = modifiers.Remove(modifiers.First(m => m.IsKind(extra.Kind())));
        }

        return method.WithModifiers(modifiers);
    }

    private static bool IsAccessibility(SyntaxToken token) =>
        token.Kind() is SyntaxKind.PrivateKeyword or SyntaxKind.ProtectedKeyword or SyntaxKind.InternalKeyword;
}
