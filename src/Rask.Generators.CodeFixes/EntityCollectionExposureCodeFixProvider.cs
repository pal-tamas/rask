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
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Simplification;

namespace Rask.Generators.CodeFixes;

// Quick-fix for RASK085 (an entity exposes a mutable collection of entities). Rewrites
//
//     public List<OrderLine> Lines { get; private set; } = new();
//
// into the shape EF Core maps through the backing field by convention:
//
//     private readonly List<OrderLine> _lines = [];
//     public IReadOnlyCollection<OrderLine> Lines => _lines;
//
// and points the references to THIS instance's property inside the declaring type at the field, so
// `Lines.Add(line)` in the entity's own method keeps compiling. Every other reference keeps the property (see
// IsThisInstance), and references outside the type are deliberately left alone: they are the callers the
// diagnostic exists to find.
//
// Withheld when the edit could not keep the meaning mechanically:
// - not an auto-property (the author already wrote a body, and the collection lives somewhere this cannot see);
// - an initializer other than none, `[]`, `new()` or an argument-free `new X<T>()` — elements or a capacity
//   would be silently dropped;
// - required / abstract / static / override / an explicit interface implementation;
// - the property is ASSIGNED inside the type — the field is readonly, so the assignment would stop compiling;
// - a member named like the field already exists;
// - the type is partial across several files — references in the other files would be missed.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EntityCollectionExposureCodeFixProvider))]
[Shared]
public sealed class EntityCollectionExposureCodeFixProvider : RaskCodeFixProvider<PropertyDeclarationSyntax>
{
    // Finds the rewritten property again once ReplaceNodes has produced a new tree.
    private static readonly SyntaxAnnotation Marker = new("RASK085_Property");

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK085"];

    protected override string Title => "Expose as IReadOnlyCollection<T> over a private field";

    protected override string EquivalenceKey => "RASK085_ReadOnlyCollection";

    protected override async Task<bool> CanFixAsync(CodeFixContext context, PropertyDeclarationSyntax node)
    {
        if (node.AccessorList is null || node.ExpressionBody is not null || node.ExplicitInterfaceSpecifier is not null
            || node.AccessorList.Accessors.Any(static a => a.Body is not null || a.ExpressionBody is not null)
            || node.Modifiers.Any(static m => m.Kind() is SyntaxKind.RequiredKeyword or SyntaxKind.AbstractKeyword
                or SyntaxKind.StaticKeyword or SyntaxKind.OverrideKeyword)
            || !IsEmptyInitializer(node.Initializer?.Value))
        {
            return false;
        }

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model?.GetDeclaredSymbol(node, context.CancellationToken) is not { } property
            || ElementOf(property.Type) is null
            || property.ContainingType.DeclaringSyntaxReferences.Select(static r => r.SyntaxTree).Distinct().Count() > 1
            || property.ContainingType.GetMembers(FieldName(property.Name)).Length > 0)
        {
            return false;
        }

        return References(node, model, property, context.CancellationToken).All(static id => !IsWrite(id));
    }

    protected override async Task<Document> FixAsync(
        Document document, PropertyDeclarationSyntax node, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model?.GetDeclaredSymbol(node, cancellationToken) is not { } property
                         || ElementOf(property.Type) is not { } element)
        {
            return document;
        }

        var fieldName = FieldName(property.Name);
        var elementSyntax = ElementSyntax(node.Type, property.Type, element)
                            ?? SyntaxFactory.ParseTypeName(element.ToMinimalDisplayString(model, node.SpanStart));

        var concrete = property.Type.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.HashSet<T>"
            ? "HashSet"
            : "List";

        // Split the property's leading trivia at its first comment: the blank line and indentation in front go
        // to the new field, the doc comment stays with the property it documents.
        var leading = node.GetLeadingTrivia();
        var split = 0;
        while (split < leading.Count && !IsComment(leading[split]))
        {
            split++;
        }

        var indentation = split > 0 && leading[split - 1].IsKind(SyntaxKind.WhitespaceTrivia)
            ? SyntaxFactory.TriviaList(leading[split - 1])
            : SyntaxFactory.TriviaList();
        var fieldLeading = split == leading.Count ? leading : SyntaxFactory.TriviaList(leading.Take(split));
        var propertyLeading = split == leading.Count
            ? indentation
            : indentation.AddRange(leading.Skip(split));

        var field = SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(Qualified(concrete, elementSyntax))
                    .AddVariables(SyntaxFactory.VariableDeclarator(fieldName)
                        .WithInitializer(SyntaxFactory.EqualsValueClause(SyntaxFactory.CollectionExpression()))))
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)))
            .NormalizeWhitespace()
            .WithLeadingTrivia(fieldLeading)
            .WithTrailingTrivia(EndOfLine(node));

        var exposed = node
            .WithType(Qualified("IReadOnlyCollection", elementSyntax).WithTriviaFrom(node.Type))
            .WithAccessorList(null)
            .WithInitializer(null)
            .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(
                SyntaxFactory.Token(SyntaxKind.EqualsGreaterThanToken).WithTrailingTrivia(SyntaxFactory.Space),
                SyntaxFactory.IdentifierName(fieldName)))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken).WithTrailingTrivia(node.GetTrailingTrivia()))
            .WithLeadingTrivia(propertyLeading)
            .WithAdditionalAnnotations(Marker);

        // Identifier keeps a trailing space before `=>`; an auto-property's identifier already has one before `{`.
        if (!exposed.Identifier.TrailingTrivia.Any(SyntaxKind.WhitespaceTrivia))
        {
            exposed = exposed.WithIdentifier(exposed.Identifier.WithTrailingTrivia(SyntaxFactory.Space));
        }

        var references = References(node, model, property, cancellationToken)
            .Where(name => IsThisInstance(name, model, property.ContainingType, cancellationToken))
            .ToList();

        var updated = root.ReplaceNodes(
            references.Cast<SyntaxNode>().Append(node),
            (original, rewritten) => original == node
                ? exposed
                : SyntaxFactory.IdentifierName(fieldName).WithTriviaFrom(rewritten));

        // Now the property is the rewritten node; put the field in front of it.
        var inserted = updated.GetAnnotatedNodes(Marker).OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        if (inserted is null)
        {
            return document;
        }

        return document.WithSyntaxRoot(updated.InsertNodesBefore(inserted, [field]));
    }

    // The document's own line ending, read off the property being replaced.
    private static SyntaxTrivia EndOfLine(PropertyDeclarationSyntax node)
    {
        foreach (var trivia in node.GetTrailingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                return trivia;
            }
        }

        return SyntaxFactory.LineFeed;
    }

    // `global::System.Collections.Generic.List<T>`, reduced by the simplifier to `List<T>` wherever the
    // namespace is already in scope — so the fix compiles in a file with no `using System.Collections.Generic`.
    private static TypeSyntax Qualified(string name, TypeSyntax element) =>
        SyntaxFactory.QualifiedName(
                SyntaxFactory.QualifiedName(
                    SyntaxFactory.QualifiedName(
                        SyntaxFactory.AliasQualifiedName(
                            SyntaxFactory.IdentifierName(SyntaxFactory.Token(SyntaxKind.GlobalKeyword)),
                            SyntaxFactory.IdentifierName("System")),
                        SyntaxFactory.IdentifierName("Collections")),
                    SyntaxFactory.IdentifierName("Generic")),
                SyntaxFactory.GenericName(SyntaxFactory.Identifier(name))
                    .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(
                        SyntaxFactory.SingletonSeparatedList(element.WithoutTrivia()))))
            .WithAdditionalAnnotations(Simplifier.Annotation);

    private static string FieldName(string property) =>
        "_" + char.ToLowerInvariant(property[0]) + property.Substring(1);

    private static bool IsComment(SyntaxTrivia trivia) =>
        trivia.Kind() is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia
            or SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia;

    private static bool IsEmptyInitializer(ExpressionSyntax? value) => value switch
    {
        null => true,
        CollectionExpressionSyntax collection => collection.Elements.Count == 0,
        ImplicitObjectCreationExpressionSyntax created =>
            created.ArgumentList.Arguments.Count == 0 && created.Initializer is null,
        ObjectCreationExpressionSyntax created =>
            (created.ArgumentList is null || created.ArgumentList.Arguments.Count == 0) && created.Initializer is null,
        _ => false,
    };

    // The T of the ICollection<T> the declared type is or implements.
    private static ITypeSymbol? ElementOf(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return null;
        }

        return new[] { named }.Concat(named.AllInterfaces)
            .FirstOrDefault(static t => t.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.ICollection<T>")
            ?.TypeArguments[0];
    }

    // Reuse the author's own spelling of the element type when the declared type is `X<Element>`.
    private static TypeSyntax? ElementSyntax(TypeSyntax declared, ITypeSymbol type, ITypeSymbol element)
    {
        var generic = declared switch
        {
            GenericNameSyntax g => g,
            QualifiedNameSyntax { Right: GenericNameSyntax g } => g,
            AliasQualifiedNameSyntax { Name: GenericNameSyntax g } => g,
            _ => null,
        };

        return generic is { TypeArgumentList.Arguments.Count: 1 }
               && type is INamedTypeSymbol { TypeArguments.Length: 1 } named
               && SymbolEqualityComparer.Default.Equals(named.TypeArguments[0], element)
            ? generic.TypeArgumentList.Arguments[0]
            : null;
    }

    // Every name in the declaring type (in this document) that binds to the property — `Lines`, `this.Lines`,
    // `other.Lines` — excluding the declaration itself, whose identifier is a token, not a name.
    private static IEnumerable<IdentifierNameSyntax> References(
        PropertyDeclarationSyntax node, SemanticModel model, IPropertySymbol property, CancellationToken cancellationToken)
    {
        if (node.Parent is not TypeDeclarationSyntax declaringType)
        {
            yield break;
        }

        foreach (var name in declaringType.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (name.Identifier.ValueText == property.Name
                && SymbolEqualityComparer.Default.Equals(
                    model.GetSymbolInfo(name, cancellationToken).Symbol?.OriginalDefinition, property.OriginalDefinition))
            {
                yield return name;
            }
        }
    }

    // Whether a reference reads the collection of THIS instance — `Lines` inside an instance member of the
    // declaring type, or `this.Lines` — which is the only kind the private field can stand in for. Everything
    // else keeps naming the property: `o.Lines` through a lambda parameter (EF's `b.HasMany(o => o.Lines)` in
    // a static Configure, where `_lines` would declare a second navigation), another instance's `other.Lines`,
    // `nameof(Lines)` (which would silently become "_lines"), and anything in a static member or nested type.
    private static bool IsThisInstance(
        IdentifierNameSyntax name, SemanticModel model, INamedTypeSymbol declaringType, CancellationToken cancellationToken)
    {
        switch (name.Parent)
        {
            case MemberAccessExpressionSyntax access when access.Name == name:
                if (access.Expression is not ThisExpressionSyntax)
                {
                    return false;
                }

                break;
            case MemberBindingExpressionSyntax:
                return false;
        }

        for (var ancestor = name.Parent; ancestor is not null and not StatementSyntax and not MemberDeclarationSyntax;
             ancestor = ancestor.Parent)
        {
            if (ancestor is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } } call
                && model.GetOperation(call, cancellationToken) is INameOfOperation)
            {
                return false;
            }
        }

        // Through lambdas and local functions to the member that owns them.
        var member = model.GetEnclosingSymbol(name.SpanStart, cancellationToken);
        while (member is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
        {
            member = member.ContainingSymbol;
        }

        return member is { IsStatic: false } && SymbolEqualityComparer.Default.Equals(member.ContainingType, declaringType);
    }

    private static bool IsWrite(IdentifierNameSyntax name)
    {
        SyntaxNode target = name.Parent is MemberAccessExpressionSyntax access && access.Name == name ? access : name;
        return target.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == target,
            ArgumentSyntax argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None),
            _ => false,
        };
    }
}
