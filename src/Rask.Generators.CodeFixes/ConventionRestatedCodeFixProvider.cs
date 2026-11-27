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
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Rask.Generators.CodeFixes;

/// <summary>
///     RASK192 — deletes a line that says what Rask's conventions already say: <c>.ToTable("fuelings")</c>,
///     <c>.HasColumnName("pistol_id")</c>, <c>.HasDatabaseName("ix_…")</c>, <c>[Table("fuelings")]</c>, and a default
///     restated — <c>.OnDelete(DeleteBehavior.Cascade)</c> on a child's key, <c>.IsRequired()</c>, <c>b.HasKey(x =&gt; x.Id);</c>.
/// </summary>
/// <remarks>
///     <para>
///         The call comes out of its chain (<c>b.Property(x =&gt; x.A).HasColumnName("a").HasMaxLength(9)</c> →
///         <c>b.Property(x =&gt; x.A).HasMaxLength(9)</c>), and a statement left saying nothing goes with it: a bare
///         <c>b;</c>, or a <c>b.Property(x =&gt; x.A);</c> over a public property EF Core maps anyway. A body left empty
///         becomes <c>{ }</c>. An attribute comes off its list, and the list with its last one.
///     </para>
///     <para>
///         Fix all does every one of a document, a project or a solution in one pass, so two in one chain both go.
///     </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ConventionRestatedCodeFixProvider))]
[Shared]
public sealed class ConventionRestatedCodeFixProvider : CodeFixProvider
{
    private const string Id = "RASK192";
    private const string Title = "Delete what Rask's conventions already say";

    private static readonly SyntaxAnnotation Spent = new("Rask192.Spent");

    // What a deleted call leaves behind: its receiver.
    private static readonly SyntaxAnnotation Left = new("Rask192.Left");

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Id];

    public override FixAllProvider GetFixAllProvider() =>
        FixAllProvider.Create(static async (context, document, diagnostics) =>
            await DeleteAsync(document, diagnostics, context.CancellationToken).ConfigureAwait(false));

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            context.RegisterCodeFix(
                CodeAction.Create(Title, ct => DeleteAsync(context.Document, [diagnostic], ct), Id),
                diagnostic);
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> DeleteAsync(Document document, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return document;
        }

        var calls = new List<InvocationExpressionSyntax>();
        var attributes = new List<AttributeSyntax>();
        foreach (var span in diagnostics.Select(static d => d.Location.SourceSpan))
        {
            var node = root.FindNode(span, getInnermostNodeForTie: true);
            if (node.FirstAncestorOrSelf<AttributeSyntax>() is { } attribute && attribute.Span == span)
            {
                attributes.Add(attribute);
            }
            else if (node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault(i => i.Span.End == span.End) is { } call)
            {
                calls.Add(call);
            }
        }

        // The statements a deleted call leaves saying nothing, decided while the property it names can still be asked about.
        var spent = calls.Select(c => c.FirstAncestorOrSelf<StatementSyntax>()).OfType<ExpressionStatementSyntax>()
            .Where(s => MapsAnyway(s.Expression, calls, model, cancellationToken)).ToList();

        var edited = root.ReplaceNodes(
            calls.Cast<SyntaxNode>().Concat(attributes).Concat(spent),
            (original, rewritten) => original switch
            {
                ExpressionStatementSyntax => rewritten.WithAdditionalAnnotations(Spent),
                AttributeSyntax => rewritten.WithAdditionalAnnotations(Spent),
                _ => Receiver((InvocationExpressionSyntax)rewritten),
            });

        return document.WithSyntaxRoot(Tidied(edited));
    }

    // `X.Name(…)` without the call: `X`, keeping the line break the call ended with.
    private static SyntaxNode Receiver(InvocationExpressionSyntax call) =>
        call.Expression is MemberAccessExpressionSyntax access
            ? access.Expression.WithTrailingTrivia(call.GetTrailingTrivia()).WithAdditionalAnnotations(Left)
            : call;

    // A statement whose chain, once the calls are out, is the builder alone or a Property(…) over a public property.
    private static bool MapsAnyway(
        ExpressionSyntax expression, List<InvocationExpressionSyntax> calls, SemanticModel model, CancellationToken cancellationToken)
    {
        var left = expression;
        while (left is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } call && calls.Contains(call))
        {
            left = access.Expression;
        }

        return left switch
        {
            IdentifierNameSyntax => true,
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Property", Expression: IdentifierNameSyntax } } property
                when !ChainHolds(expression, property, calls) =>
                property.ArgumentList.Arguments.Count == 1 &&
                property.ArgumentList.Arguments[0].Expression is SimpleLambdaExpressionSyntax { Body: MemberAccessExpressionSyntax member } &&
                model.GetSymbolInfo(member, cancellationToken).Symbol is IPropertySymbol
                {
                    DeclaredAccessibility: Accessibility.Public, GetMethod.DeclaredAccessibility: Accessibility.Public, IsStatic: false,
                },
            _ => false,
        };
    }

    // Whether anything but the deleted calls follows `property` in the statement's chain.
    private static bool ChainHolds(ExpressionSyntax expression, InvocationExpressionSyntax property, List<InvocationExpressionSyntax> calls)
    {
        for (var step = expression; step != property; step = ((MemberAccessExpressionSyntax)((InvocationExpressionSyntax)step).Expression).Expression)
        {
            if (step is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax } call || !calls.Contains(call))
            {
                return true;
            }
        }

        return false;
    }

    // Takes out the spent statements and attributes, and gives a body left saying nothing a `{ }`.
    private static SyntaxNode Tidied(SyntaxNode root)
    {
        var attributes = root.GetAnnotatedNodes(Spent).OfType<AttributeSyntax>().ToList();
        root = root.ReplaceNodes(
            attributes.Select(static a => a.Parent!).Distinct(),
            static (_, list) =>
            {
                var kept = ((AttributeListSyntax)list).Attributes.Where(static a => !a.HasAnnotation(Spent)).ToList();
                return kept.Count == 0 ? list.WithAdditionalAnnotations(Spent) : ((AttributeListSyntax)list).WithAttributes(SeparatedList(kept));
            });

        // A statement goes with its line; an attribute list leaves what precedes it — the type's doc comment — in place.
        root = root.RemoveNodes(
            root.GetAnnotatedNodes(Spent).OfType<ExpressionStatementSyntax>(),
            SyntaxRemoveOptions.KeepNoTrivia | SyntaxRemoveOptions.KeepDirectives) ?? root;
        root = root.RemoveNodes(
            root.GetAnnotatedNodes(Spent).OfType<AttributeListSyntax>(),
            SyntaxRemoveOptions.KeepLeadingTrivia | SyntaxRemoveOptions.KeepDirectives) ?? root;

        var bare = root.DescendantNodes().Where(static n =>
            n is ArrowExpressionClauseSyntax { Expression: IdentifierNameSyntax arrow } && arrow.HasAnnotation(Left) ||
            n is LambdaExpressionSyntax { ExpressionBody: IdentifierNameSyntax body } && body.HasAnnotation(Left)).ToList();
        return root.ReplaceNodes(
            bare.Select(static n => n is ArrowExpressionClauseSyntax arrow ? arrow.Parent! : n),
            static (_, rewritten) => rewritten switch
            {
                MethodDeclarationSyntax method => method.WithExpressionBody(null).WithSemicolonToken(default)
                    .WithBody(Block()).WithTriviaFrom(method),
                LocalFunctionStatementSyntax local => local.WithExpressionBody(null).WithSemicolonToken(default).WithBody(Block()),
                LambdaExpressionSyntax lambda => lambda.WithExpressionBody(null).WithBody(Block()),
                _ => rewritten,
            });
    }
}
