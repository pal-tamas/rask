using System;
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
using Microsoft.CodeAnalysis.Simplification;

namespace Rask.Generators.CodeFixes;

// Quick-fix for RASK196 (another aggregate's id held by hand). `public VehicleId? VehicleId { get; private set; }` becomes
// `public partial BelongsTo<Vehicle>? Vehicle { get; }` — optional when the id was nullable, required when it was not —
// which generates the same VehicleId, so every use of the id in the type still compiles. A hand-written `Vehicle`
// navigation to the same aggregate is removed, and the type (with every type it is nested in) is made partial.
//
// Withheld when the edit could not keep the meaning mechanically:
// - the id carries attributes, an initializer, an accessor with a body, or a setter that is not private,
// - the navigation it replaces is used in the type, has a body, or carries attributes,
// - the type is partial across several files — a member in another file would be missed, or
// - Rask.Data's BelongsTo is not referenced.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HeldReferenceCodeFixProvider))]
[Shared]
public sealed class HeldReferenceCodeFixProvider : CodeFixProvider
{
    // The analyzer's property keys (Rask.Batteries.Generators' HeldReferenceAnalyzer), repeated: this assembly cannot
    // reference that one.
    private const string NavigationProperty = "Navigation";
    private const string TargetProperty = "Target";

    private const string Title = "Declare the reference as a BelongsTo";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK196"];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<PropertyDeclarationSyntax>() is not { } node ||
            !diagnostic.Properties.TryGetValue(NavigationProperty, out var navigation) || navigation is null ||
            !diagnostic.Properties.TryGetValue(TargetProperty, out var target) || target is null ||
            await PlanAsync(context.Document, node, navigation, target, context.CancellationToken).ConfigureAwait(false) is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => FixAsync(context.Document, node, navigation, target, ct), "RASK196_BelongsTo"),
            diagnostic);
    }

    private static async Task<Document> FixAsync(
        Document document, PropertyDeclarationSyntax node, string navigation, string target, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || await PlanAsync(document, node, navigation, target, cancellationToken).ConfigureAwait(false) is not { } plan)
        {
            return document;
        }

        var reference = Reference(node, navigation, plan.Target, plan.Optional);
        var replaced = plan.Navigation is null ? [node] : new SyntaxNode[] { node, plan.Navigation };
        var updated = root.ReplaceNodes(replaced, (original, _) => original == node ? reference : original.WithAdditionalAnnotations(Gone));

        if (updated.GetAnnotatedNodes(Gone).FirstOrDefault() is { } gone)
        {
            updated = updated.RemoveNode(gone, SyntaxRemoveOptions.KeepNoTrivia) ?? updated;
        }

        return document.WithSyntaxRoot(MadePartial(updated, reference));
    }

    // Finds the hand-written navigation again once the reference has replaced the id.
    private static readonly SyntaxAnnotation Gone = new("RASK196_Navigation");

    private static async Task<Plan?> PlanAsync(
        Document document, PropertyDeclarationSyntax node, string navigation, string target, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model?.GetDeclaredSymbol(node, cancellationToken) is not { } property ||
            property.ContainingType.DeclaringSyntaxReferences.Length > 1 ||
            model.Compilation.GetTypeByMetadataName("Rask.Data.BelongsTo`1") is null ||
            model.Compilation.GetTypeByMetadataName(target) is not { } aggregate ||
            !IsPlainId(node) ||
            node.Parent is not TypeDeclarationSyntax holder)
        {
            return null;
        }

        PropertyDeclarationSyntax? held = null;
        foreach (var member in property.ContainingType.GetMembers(navigation))
        {
            var syntax = member.DeclaringSyntaxReferences.FirstOrDefault() is { } reference
                ? await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false)
                : null;
            if (syntax is not PropertyDeclarationSyntax declaration ||
                declaration.AttributeLists.Count > 0 || HasBody(declaration) || IsUsed(member, holder, declaration, model, cancellationToken))
            {
                return null;
            }

            held = declaration;
        }

        var optional = property.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                       property.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
        return new Plan(aggregate, held, optional);
    }

    // `{ get; private set; }`, `{ get; }`, `{ get; private init; }`: nothing on it but what a generated key repeats.
    private static bool IsPlainId(PropertyDeclarationSyntax node) =>
        node.AttributeLists.Count == 0 &&
        node.Initializer is null &&
        node.AccessorList is { } accessors &&
        !HasBody(node) &&
        accessors.Accessors.All(static a =>
            a.Keyword.IsKind(SyntaxKind.GetKeyword) || a.Modifiers.Any(static m => m.IsKind(SyntaxKind.PrivateKeyword)));

    private static bool HasBody(PropertyDeclarationSyntax node) =>
        node.ExpressionBody is not null ||
        node.AccessorList?.Accessors.Any(static a => a.Body is not null || a.ExpressionBody is not null) == true;

    private static bool IsUsed(ISymbol member, TypeDeclarationSyntax holder, PropertyDeclarationSyntax declaration, SemanticModel model, CancellationToken cancellationToken) =>
        holder.DescendantNodes().OfType<IdentifierNameSyntax>().Any(name =>
            string.Equals(name.Identifier.ValueText, member.Name, StringComparison.Ordinal) &&
            !name.Ancestors().Contains(declaration) &&
            SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name, cancellationToken).Symbol, member));

    private static PropertyDeclarationSyntax Reference(PropertyDeclarationSyntax node, string navigation, INamedTypeSymbol target, bool optional)
    {
        // `global::Rask.Data.BelongsTo<global::Fleet.Vehicle>`, reduced by the simplifier where the namespaces are imported.
        TypeSyntax type = SyntaxFactory.ParseTypeName(
                "global::Rask.Data.BelongsTo<" + target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">")
            .WithAdditionalAnnotations(Simplifier.Annotation);
        if (optional)
        {
            type = SyntaxFactory.NullableType(type);
        }

        var modifiers = SyntaxFactory.TokenList(
            node.Modifiers.Where(static m => !m.IsKind(SyntaxKind.PartialKeyword))
                .Append(SyntaxFactory.Token(SyntaxKind.PartialKeyword))
                .Select(static m => m.WithTrailingTrivia(SyntaxFactory.Space)));

        return SyntaxFactory.PropertyDeclaration(type.WithTrailingTrivia(SyntaxFactory.Space), SyntaxFactory.Identifier(navigation).WithTrailingTrivia(SyntaxFactory.Space))
            .WithModifiers(modifiers)
            .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))))
                .NormalizeWhitespace())
            .WithLeadingTrivia(node.GetLeadingTrivia())
            .WithTrailingTrivia(node.GetTrailingTrivia());
    }

    // The type holding the reference, and each type it is nested in, made partial: the generated half joins them.
    private static SyntaxNode MadePartial(SyntaxNode root, PropertyDeclarationSyntax reference)
    {
        var placed = root.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .First(p => string.Equals(p.Identifier.ValueText, reference.Identifier.ValueText, StringComparison.Ordinal) &&
                        p.Modifiers.Any(SyntaxKind.PartialKeyword));
        var holders = placed.Ancestors().OfType<TypeDeclarationSyntax>().Where(static t => !t.Modifiers.Any(SyntaxKind.PartialKeyword)).ToList();

        return root.ReplaceNodes(holders, static (_, rewritten) =>
        {
            var type = rewritten;
            var partial = SyntaxFactory.Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(SyntaxFactory.Space);
            return type.Modifiers.Count == 0
                ? type.WithModifiers(SyntaxFactory.TokenList(partial.WithLeadingTrivia(type.Keyword.LeadingTrivia)))
                    .WithKeyword(type.Keyword.WithLeadingTrivia())
                : type.WithModifiers(type.Modifiers.Add(partial));
        });
    }

    private sealed class Plan(INamedTypeSymbol target, PropertyDeclarationSyntax? navigation, bool optional)
    {
        public INamedTypeSymbol Target { get; } = target;

        public PropertyDeclarationSyntax? Navigation { get; } = navigation;

        public bool Optional { get; } = optional;
    }
}
