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

    protected override async Task<bool> CanFixAsync(CodeFixContext context, PropertyDeclarationSyntax property)
    {
        if (property.Modifiers.Any(SyntaxKind.RequiredKeyword))
        {
            return false;
        }

        // Guard against trading RASK001 (a benign hint) for RASK002: when the component is built through
        // a dependency-injected constructor with no public parameterless ctor, the generated factory uses
        // ActivatorUtilities.CreateInstance and cannot set a `required` property — the generator would then
        // emit RASK002 and the DI constructor's services would be null. Only offer the fix when `required`
        // can actually be honored (mirrors the RASK002 trigger in ComponentFactoryGenerator).
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model?.GetDeclaredSymbol(property, context.CancellationToken) is not { ContainingType: { } type })
        {
            return true; // no semantic info — best-effort offer, matching the always-on prior behavior
        }

        var instanceCtors = type.InstanceConstructors.Where(c => !c.IsStatic).ToArray();
        var hasParameterless = instanceCtors.Any(c => c.Parameters.Length == 0);
        var hasDIConstructor = instanceCtors.Any(c => c.Parameters.Length > 0);
        return !(hasDIConstructor && !hasParameterless);
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
