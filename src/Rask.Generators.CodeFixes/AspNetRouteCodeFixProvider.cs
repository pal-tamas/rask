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

// Quick-fix for RASK071 (ASP.NET's [Route] on a Rask component): point the attribute at Rask's own
// route attribute, keeping the template argument exactly as written.
//
// Only the NAME is replaced, never the whole attribute, so the template and any trivia survive. The
// replacement is written fully qualified and handed to Roslyn's simplifier rather than shortened here:
// the reducer verifies that the short form binds to the same symbol before it applies, which is what
// makes this safe in the very file that provoked the diagnostic. Where Microsoft.AspNetCore.Mvc is
// imported a bare Route would bind to MVC's attribute again — or be ambiguous — so the qualified form
// simply stays, and the fix can never trade RASK071 for CS0104.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AspNetRouteCodeFixProvider))]
[Shared]
public sealed class AspNetRouteCodeFixProvider : RaskCodeFixProvider<AttributeSyntax>
{
    // Without the Attribute suffix: this is attribute position, where C# accepts the short form, and it
    // is the spelling the simplifier reduces to a bare Route when that is unambiguous.
    private const string RaskRouteName = "Rask.Core.Routing.Route";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("RASK071");

    protected override string Title => "Use Rask's [Route]";

    protected override string EquivalenceKey => "RASK071_UseRaskRoute";

    // Rask's attribute is RouteAttribute(string template) with a get-only Template, so it accepts
    // exactly one positional argument and no property initialisers. MVC's takes the same template PLUS
    // settable Name and Order, and the arguments are carried over verbatim — so an untested
    // `[Route("/orders", Name = "orders", Order = 2)]` would come back as CS0117 on a property Rask's
    // attribute does not have, and an alias with a baked-in template and no arguments at all would come
    // back as CS7036. Both are a worse diagnostic than the one being fixed, so the lightbulb is
    // withheld instead and the developer moves the attribute over deliberately.
    protected override Task<bool> CanFixAsync(CodeFixContext context, AttributeSyntax node)
    {
        var arguments = node.ArgumentList?.Arguments;

        return Task.FromResult(
            arguments is { Count: 1 }
            // `NameEquals` is the `Name = "x"` property-initialiser form. `NameColon`
            // (`template: "/x"`) names the constructor parameter, which Rask's attribute also calls
            // `template`, so that one carries over intact.
            && arguments.Value[0].NameEquals is null);
    }

    protected override Task<Document> FixAsync(
        Document document, AttributeSyntax node, CancellationToken cancellationToken)
    {
        var qualified = SyntaxFactory.ParseName(RaskRouteName)
            .WithAdditionalAnnotations(Simplifier.Annotation, Simplifier.AddImportsAnnotation)
            .WithTriviaFrom(node.Name);

        return ReplaceNodeAsync(document, node.Name, qualified, cancellationToken);
    }
}
