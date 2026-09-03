using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace Rask.Generators.CodeFixes;

// Quick-fix for RASK067 (ASP.NET's [Route] on a Rask component): point the attribute at Rask's own
// route attribute, keeping the template argument exactly as written.
//
// Only the NAME is replaced, never the whole attribute, so the template and any trivia survive. The
// replacement is written fully qualified and handed to Roslyn's simplifier rather than shortened here:
// the reducer verifies that the short form binds to the same symbol before it applies, which is what
// makes this safe in the very file that provoked the diagnostic. Where Microsoft.AspNetCore.Mvc is
// imported a bare Route would bind to MVC's attribute again — or be ambiguous — so the qualified form
// simply stays, and the fix can never trade RASK067 for CS0104.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AspNetRouteCodeFixProvider))]
[Shared]
public sealed class AspNetRouteCodeFixProvider : RaskCodeFixProvider<AttributeSyntax>
{
    // Without the Attribute suffix: this is attribute position, where C# accepts the short form, and it
    // is the spelling the simplifier reduces to a bare Route when that is unambiguous.
    private const string RaskRouteName = "Rask.Core.Routing.Route";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("RASK067");

    protected override string Title => "Use Rask's [Route]";

    protected override string EquivalenceKey => "RASK067_UseRaskRoute";

    protected override Task<Document> FixAsync(
        Document document, AttributeSyntax node, CancellationToken cancellationToken)
    {
        var qualified = SyntaxFactory.ParseName(RaskRouteName)
            .WithAdditionalAnnotations(Simplifier.Annotation, Simplifier.AddImportsAnnotation)
            .WithTriviaFrom(node.Name);

        return ReplaceNodeAsync(document, node.Name, qualified, cancellationToken);
    }
}
