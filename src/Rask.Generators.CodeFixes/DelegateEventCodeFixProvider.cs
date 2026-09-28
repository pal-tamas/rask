using System.Collections.Generic;
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

// Quick-fix for RASK096 (an event declared as a delegate): `Action<Order>? OnSave` → `Callback<Order> OnSave`,
// and — inside the same type declaration — every place it fires, since a Callback is not invocable and never
// null: `OnSave?.Invoke(x)` and `OnSave(x)` become `OnSave.Invoke(x)`, awaited when the statement sits in an
// async method or lambda. In a synchronous one the ValueTask is left for the author (RASK093 says so).
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DelegateEventCodeFixProvider))]
[Shared]
public sealed class DelegateEventCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("RASK096");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        if (root?.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<PropertyDeclarationSyntax>() is not { } property)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Declare it as a Callback",
                ct => FixAsync(context.Document, property, ct),
                "RASK096_DeclareAsCallback"),
            diagnostic);
    }

    private static async Task<Document> FixAsync(
        Document document, PropertyDeclarationSyntax property, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model?.GetDeclaredSymbol(property, cancellationToken) is not { } symbol
            || Callback(property.Type, symbol.Type) is not { } callback)
        {
            return document;
        }

        var replacements = new Dictionary<SyntaxNode, SyntaxNode>
        {
            [property.Type] = callback.WithTriviaFrom(property.Type),
        };

        if (property.Parent is TypeDeclarationSyntax owner)
        {
            foreach (var node in owner.DescendantNodes())
            {
                if (Fire(node, symbol, model, cancellationToken) is { } fired)
                {
                    replacements[node] = fired;
                }
            }
        }

        return document.WithSyntaxRoot(root.ReplaceNodes(replacements.Keys, (original, _) => replacements[original]));
    }

    // `Callback<Order>` from `Action<Order>?` / `Func<Order, Task>`, reusing the type arguments as written.
    private static TypeSyntax? Callback(TypeSyntax written, ITypeSymbol type)
    {
        var bare = written is NullableTypeSyntax nullable ? nullable.ElementType : written;
        var name = bare is QualifiedNameSyntax qualified ? qualified.Right : bare as SimpleNameSyntax;
        if (name is null || type is not INamedTypeSymbol delegateType)
        {
            return null;
        }

        var arguments = name is GenericNameSyntax generic
            ? generic.TypeArgumentList.Arguments.ToList()
            : new List<TypeSyntax>();
        if (delegateType.Name == "Func" && arguments.Count > 0)
        {
            arguments.RemoveAt(arguments.Count - 1);
        }

        SimpleNameSyntax callback = arguments.Count == 0
            ? SyntaxFactory.IdentifierName("Callback")
            : SyntaxFactory.GenericName(SyntaxFactory.Identifier("Callback"))
                .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(
                    SyntaxFactory.SeparatedList(arguments.Select(static a => a.WithoutTrivia()))));
        // Fully qualified and marked for simplification: `Callback<Order>` wherever Rask.Core is imported.
        var rask = SyntaxFactory.QualifiedName(
            SyntaxFactory.AliasQualifiedName(
                SyntaxFactory.IdentifierName(SyntaxFactory.Token(SyntaxKind.GlobalKeyword)),
                SyntaxFactory.IdentifierName("Rask")),
            SyntaxFactory.IdentifierName("Core"));
        return SyntaxFactory.QualifiedName(rask, callback).WithAdditionalAnnotations(Simplifier.Annotation);
    }

    // `OnSave?.Invoke(x)` / `OnSave(x)` / `OnSave.Invoke(x)` → `OnSave.Invoke(x)`, awaited where it can be.
    private static ExpressionSyntax? Fire(
        SyntaxNode node, IPropertySymbol property, SemanticModel model, CancellationToken cancellationToken)
    {
        ExpressionSyntax target;
        ArgumentListSyntax arguments;
        switch (node)
        {
            case ConditionalAccessExpressionSyntax
            {
                WhenNotNull: InvocationExpressionSyntax
                {
                    Expression: MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Invoke" },
                } call,
            } access when Is(access.Expression, property, model, cancellationToken):
                target = access.Expression;
                arguments = call.ArgumentList;
                break;
            case InvocationExpressionSyntax call when Is(call.Expression, property, model, cancellationToken):
                target = call.Expression;
                arguments = call.ArgumentList;
                break;
            case InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Invoke" } member,
            } call when Is(member.Expression, property, model, cancellationToken):
                target = member.Expression;
                arguments = call.ArgumentList;
                break;
            default:
                return null;
        }

        ExpressionSyntax fired = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression, target.WithoutTrivia(), SyntaxFactory.IdentifierName("Invoke")),
            arguments);
        if (node.Parent is ExpressionStatementSyntax && InAsyncBody(node))
        {
            fired = SyntaxFactory.AwaitExpression(fired);
        }

        return node is InvocationExpressionSyntax && fired is InvocationExpressionSyntax
               && node.IsEquivalentTo(fired)
            ? null
            : fired.WithTriviaFrom(node);
    }

    private static bool Is(ExpressionSyntax expression, IPropertySymbol property, SemanticModel model, CancellationToken ct) =>
        expression is IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }
        && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(expression, ct).Symbol, property);

    // The nearest enclosing function body is async.
    private static bool InAsyncBody(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax lambda:
                    return lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
                case LocalFunctionStatementSyntax local:
                    return local.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case MethodDeclarationSyntax method:
                    return method.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }
}
