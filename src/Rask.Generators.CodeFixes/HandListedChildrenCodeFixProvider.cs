using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace Rask.Generators.CodeFixes;

// Quick-fix for RASK192 (a child list exposed by hand). Rewrites the aggregate's children into one HasMany of them: a
// private List of Pistol named _pistols with a read-only view named Pistols becomes one HasMany property named
// Pistols, initialized empty, the field goes, and every use of the field in the aggregate names the property instead
// (AddRange is called as Add). A public List or ICollection property becomes the same HasMany property, its AddRange
// calls Add. Callers outside the aggregate are left alone: changing the children there is what RASK180 then reports.
//
// Withheld when the edit could not keep the meaning mechanically:
// - the list is used in a way HasMany has no member for — Insert, Sort, an index written, a List<X> parameter,
//   RemoveAll given a method group (HasMany's takes a Func, List's a Predicate),
// - the list (or the public property) is assigned anywhere but its own empty initializer,
// - the type is partial across several files — references in the other files would be missed, or
// - Rask.Data's HasMany is not referenced.
// A Sync the codefix would need DistinctBy for never arises: it rewrites calls, never adds a Sync.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HandListedChildrenCodeFixProvider))]
[Shared]
public sealed class HandListedChildrenCodeFixProvider : RaskCodeFixProvider<PropertyDeclarationSyntax>
{
    // Finds the field again once ReplaceNodes has produced a new tree.
    private static readonly SyntaxAnnotation FieldMarker = new("RASK192_Field");

    // The List<T> members HasMany<T> answers to by the same name, and AddRange, which it answers to as Add.
    private static readonly ImmutableHashSet<string> Kept =
        ImmutableHashSet.Create(StringComparer.Ordinal, "Add", "AddRange", "Remove", "RemoveAll", "Clear", "Count");

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK192"];

    protected override string Title => "Declare the children as one HasMany<T>";

    protected override string EquivalenceKey => "RASK192_HasMany";

    protected override async Task<bool> CanFixAsync(CodeFixContext context, PropertyDeclarationSyntax node) =>
        await PlanAsync(context.Document, node, context.CancellationToken).ConfigureAwait(false) is not null;

    protected override async Task<Document> FixAsync(
        Document document, PropertyDeclarationSyntax node, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || await PlanAsync(document, node, cancellationToken).ConfigureAwait(false) is not { } plan)
        {
            return document;
        }

        var replaced = plan.References.Cast<SyntaxNode>()
            .Concat(plan.AddRanges)
            .Append(node)
            .Concat(plan.Field is null ? [] : [plan.Field]);

        var updated = root.ReplaceNodes(replaced, (original, rewritten) => original switch
        {
            PropertyDeclarationSyntax => HasManyProperty(node, plan.Element),
            VariableDeclaratorSyntax or FieldDeclarationSyntax => rewritten.WithAdditionalAnnotations(FieldMarker),
            IdentifierNameSyntax name when plan.AddRanges.Contains(name) =>
                SyntaxFactory.IdentifierName("Add").WithTriviaFrom(rewritten),
            _ => SyntaxFactory.IdentifierName(node.Identifier.ValueText).WithTriviaFrom(rewritten),
        });

        if (updated.GetAnnotatedNodes(FieldMarker).FirstOrDefault() is { } field)
        {
            updated = updated.RemoveNode(field, SyntaxRemoveOptions.KeepNoTrivia) ?? updated;
        }

        return document.WithSyntaxRoot(updated);
    }

    // What the fix changes, or null when it cannot change it mechanically.
    private static async Task<Plan?> PlanAsync(Document document, PropertyDeclarationSyntax node, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model?.GetDeclaredSymbol(node, cancellationToken) is not { Type: INamedTypeSymbol { TypeArguments.Length: 1 } held } property
            || node.Parent is not TypeDeclarationSyntax declaringType
            || property.ContainingType.DeclaringSyntaxReferences.Length > 1
            || model.Compilation.GetTypeByMetadataName("Rask.Data.HasMany`1") is not { } hasManyOf)
        {
            return null;
        }

        var element = held.TypeArguments[0];
        var hasMany = hasManyOf.Construct(element);
        var elementSyntax = ElementSyntax(node.Type) ?? SyntaxFactory.ParseTypeName(element.ToMinimalDisplayString(model, node.SpanStart));

        // A view over a private list: the list is what the aggregate changes. A public list: the property itself is.
        ISymbol changed = property;
        SyntaxNode? fieldNode = null;
        if (BackingField(node, model, cancellationToken) is { } backing)
        {
            var declared = backing.Field.DeclaringSyntaxReferences.FirstOrDefault() is { } reference
                ? await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false)
                : null;
            if (declared is not VariableDeclaratorSyntax declarator
                || !IsEmpty(declarator.Initializer?.Value)
                || declarator.Parent?.Parent is not FieldDeclarationSyntax fieldDeclaration)
            {
                return null;
            }

            changed = backing.Field;
            fieldNode = fieldDeclaration.Declaration.Variables.Count == 1 ? fieldDeclaration : declarator;
        }
        else if (node.AccessorList is null || node.AccessorList.Accessors.Any(static a => a.Body is not null || a.ExpressionBody is not null)
                 || !IsEmpty(node.Initializer?.Value))
        {
            return null;
        }

        var addRanges = new List<IdentifierNameSyntax>();
        var uses = UsesOf(changed, declaringType, node, model, cancellationToken);
        if (!uses.TrueForAll(use => IsKeptUse(use, model, hasMany, addRanges, cancellationToken)))
        {
            return null;
        }

        // The field's uses are renamed to the property; a public list's already name it.
        return new Plan(elementSyntax, fieldNode, fieldNode is null ? [] : uses, addRanges);
    }

    // Every name in the declaring type that binds to the list, but for the property's own getter.
    private static List<IdentifierNameSyntax> UsesOf(
        ISymbol changed, TypeDeclarationSyntax declaringType, PropertyDeclarationSyntax node, SemanticModel model, CancellationToken cancellationToken) =>
        [.. declaringType.DescendantNodes().OfType<IdentifierNameSyntax>().Where(name =>
            string.Equals(name.Identifier.ValueText, changed.Name, StringComparison.Ordinal)
            && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name, cancellationToken).Symbol?.OriginalDefinition, changed.OriginalDefinition)
            && !name.Ancestors().Contains(node))];

    // Whether HasMany<X> does what the list did here: the members it shares, LINQ, a foreach, an index read, an
    // argument whose parameter takes a HasMany. AddRange is noted, to be called as Add.
    private static bool IsKeptUse(
        IdentifierNameSyntax name, SemanticModel model, INamedTypeSymbol hasMany, List<IdentifierNameSyntax> addRanges, CancellationToken cancellationToken)
    {
        ExpressionSyntax use = name.Parent is MemberAccessExpressionSyntax self && self.Name == name ? self : name;
        switch (use.Parent)
        {
            case MemberAccessExpressionSyntax access when access.Expression == use:
                var member = model.GetSymbolInfo(access.Name, cancellationToken).Symbol;
                if (member is IMethodSymbol { IsExtensionMethod: true })
                {
                    return true;
                }

                var called = access.Name.Identifier.ValueText;
                if (!Kept.Contains(called)
                    || (string.Equals(called, "RemoveAll", StringComparison.Ordinal) && !IsGivenALambda(access)))
                {
                    return false;
                }

                if (string.Equals(called, "AddRange", StringComparison.Ordinal) && access.Name is IdentifierNameSyntax range)
                {
                    addRanges.Add(range);
                }

                return true;
            case ElementAccessExpressionSyntax index when index.Expression == use:
                return index.Parent is not AssignmentExpressionSyntax assignment || assignment.Left != index;
            case ForEachStatementSyntax loop when loop.Expression == use:
                return true;
            case ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.None } argument:
                return model.GetOperation(argument, cancellationToken) is Microsoft.CodeAnalysis.Operations.IArgumentOperation { Parameter: { } parameter }
                       && model.Compilation.ClassifyConversion(hasMany, parameter.Type).IsImplicit;
            default:
                return false;
        }
    }

    // The private List<X> field the view's getter hands out.
    private static (IFieldSymbol Field, IdentifierNameSyntax Name)? BackingField(
        PropertyDeclarationSyntax node, SemanticModel model, CancellationToken cancellationToken)
    {
        SyntaxNode? getter = node.ExpressionBody?.Expression;
        if (getter is null && node.AccessorList?.Accessors.FirstOrDefault(static a => a.Keyword.IsKind(SyntaxKind.GetKeyword)) is { } get)
        {
            getter = (SyntaxNode?)get.ExpressionBody?.Expression ?? get.Body;
        }

        foreach (var name in getter?.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>() ?? [])
        {
            if (model.GetSymbolInfo(name, cancellationToken).Symbol is IFieldSymbol { DeclaredAccessibility: Accessibility.Private } field
                && string.Equals(field.Type.OriginalDefinition.ToDisplayString(), "System.Collections.Generic.List<T>", StringComparison.Ordinal))
            {
                return (field, name);
            }
        }

        return null;
    }

    private static PropertyDeclarationSyntax HasManyProperty(PropertyDeclarationSyntax node, TypeSyntax element)
    {
        // `global::Rask.Data.HasMany<X>`, reduced by the simplifier to `HasMany<X>` where Rask.Data is imported.
        var qualified = SyntaxFactory.QualifiedName(
                SyntaxFactory.QualifiedName(
                    SyntaxFactory.AliasQualifiedName(
                        SyntaxFactory.IdentifierName(SyntaxFactory.Token(SyntaxKind.GlobalKeyword)),
                        SyntaxFactory.IdentifierName("Rask")),
                    SyntaxFactory.IdentifierName("Data")),
                SyntaxFactory.GenericName(SyntaxFactory.Identifier("HasMany"))
                    .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(element.WithoutTrivia()))))
            .WithAdditionalAnnotations(Simplifier.Annotation);

        return SyntaxFactory.PropertyDeclaration(qualified.WithTrailingTrivia(SyntaxFactory.Space), node.Identifier.WithTrailingTrivia(SyntaxFactory.Space))
            .WithAttributeLists(node.AttributeLists)
            .WithModifiers(node.Modifiers)
            .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))))
                .NormalizeWhitespace()
                .WithTrailingTrivia(SyntaxFactory.Space))
            .WithInitializer(SyntaxFactory.EqualsValueClause(
                SyntaxFactory.Token(SyntaxKind.EqualsToken).WithTrailingTrivia(SyntaxFactory.Space),
                SyntaxFactory.ImplicitObjectCreationExpression()))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            .WithLeadingTrivia(node.GetLeadingTrivia())
            .WithTrailingTrivia(node.GetTrailingTrivia());
    }

    // RemoveAll's argument, which HasMany takes as a Func: a lambda converts to it, a Predicate method group does not.
    private static bool IsGivenALambda(MemberAccessExpressionSyntax access) =>
        access.Parent is InvocationExpressionSyntax call
        && call.ArgumentList.Arguments.Count == 1
        && call.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax;

    // The author's own spelling of X in `List<X>` / `IReadOnlyList<X>`.
    private static TypeSyntax? ElementSyntax(TypeSyntax declared)
    {
        var generic = declared switch
        {
            GenericNameSyntax g => g,
            QualifiedNameSyntax { Right: GenericNameSyntax g } => g,
            _ => null,
        };

        return generic is { TypeArgumentList.Arguments.Count: 1 } ? generic.TypeArgumentList.Arguments[0] : null;
    }

    private static bool IsEmpty(ExpressionSyntax? value) => value switch
    {
        null => true,
        CollectionExpressionSyntax collection => collection.Elements.Count == 0,
        ImplicitObjectCreationExpressionSyntax created => created.ArgumentList.Arguments.Count == 0 && created.Initializer is null,
        ObjectCreationExpressionSyntax created =>
            (created.ArgumentList is null || created.ArgumentList.Arguments.Count == 0) && created.Initializer is null,
        _ => false,
    };

    private sealed class Plan(
        TypeSyntax elementSyntax,
        SyntaxNode? field,
        IReadOnlyList<IdentifierNameSyntax> references,
        IReadOnlyList<IdentifierNameSyntax> addRanges)
    {
        public TypeSyntax Element { get; } = elementSyntax;

        public SyntaxNode? Field { get; } = field;

        public IReadOnlyList<IdentifierNameSyntax> References { get; } = references;

        public IReadOnlyList<IdentifierNameSyntax> AddRanges { get; } = addRanges;
    }
}
