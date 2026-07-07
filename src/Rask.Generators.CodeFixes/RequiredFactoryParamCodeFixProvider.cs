using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.CodeFixes;

// Quick-fix for RASK001 (a non-nullable, no-initializer property becomes a required factory
// parameter): add the `required` modifier so the language enforces at every call site what the
// generated factory already enforces. Exactly the "consider also marking it 'required'" the diagnostic
// message recommends.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RequiredFactoryParamCodeFixProvider))]
[Shared]
public sealed class RequiredFactoryParamCodeFixProvider : RaskCodeFixProvider<PropertyDeclarationSyntax>
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("RASK001");

    protected override string Title => "Add 'required' modifier";

    protected override string EquivalenceKey => "RASK001_AddRequired";

    protected override Task<bool> CanFixAsync(CodeFixContext context, PropertyDeclarationSyntax property)
    {
        // RASK001 only targets a non-nullable, no-initializer factory param, and the generated factory
        // always honors `required` on such a prop — the DI-ctor path builds via ActivatorUtilities and
        // post-assigns it, the object-init path sets it in the initializer. RASK002 only fires for a
        // required prop carrying a member initializer, which is never the RASK001 case, so adding
        // `required` here can never trade the hint for a RASK002 warning. Always offer the fix.
        return Task.FromResult(!property.Modifiers.Any(SyntaxKind.RequiredKeyword));
    }

    protected override Task<Document> FixAsync(
        Document document, PropertyDeclarationSyntax property, CancellationToken cancellationToken)
    {
        var required = SyntaxFactory.Token(SyntaxKind.RequiredKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);
        // Appended to the modifier list; for the public settable properties that RASK001 targets this
        // yields the conventional `public required T`.
        var newProperty = property.AddModifiers(required);
        return ReplaceNodeAsync(document, property, newProperty, cancellationToken);
    }
}
