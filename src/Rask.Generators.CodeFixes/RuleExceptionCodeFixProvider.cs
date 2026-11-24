using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Rask.Generators.CodeFixes;

/// <summary>
///     RASK184 — an aggregate's exception becomes a refusal a form shows:
///     <c>if (c) throw new InvalidOperationException(m);</c> → <c>Refuse.When(c, m);</c>, <c>if (!c) …</c> →
///     <c>Refuse.Unless(c, m);</c>, <c>throw new ArgumentException(m, nameof(x))</c> under an <c>if</c> →
///     <c>Refuse.When(c, () =&gt; x, m);</c>, and any other throw of one → <c>throw new RaskValidationException(m)</c>.
/// </summary>
/// <remarks>
///     Offered only for an exception built with its message (and, for an <c>ArgumentException</c> under an <c>if</c>,
///     a <c>nameof</c> parameter name; elsewhere, an inner exception): one built with nothing has no sentence to show.
///     <c>using Rask.Data;</c> / <c>using Rask.Wire;</c> is added where the short name does not bind.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RuleExceptionCodeFixProvider))]
[Shared]
public sealed class RuleExceptionCodeFixProvider : RaskCodeFixProvider<ObjectCreationExpressionSyntax>
{
    private const string RefuseNamespace = "Rask.Data";
    private const string ExceptionNamespace = "Rask.Wire";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK184"];

    protected override string Title => "Refuse with a message a form shows";

    protected override string EquivalenceKey => "RASK184_Refuse";

    protected override async Task<bool> CanFixAsync(CodeFixContext context, ObjectCreationExpressionSyntax node)
    {
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        return model is not null && (Guarded(model, node, context.CancellationToken) is not null || Rethrown(model, node, context.CancellationToken));
    }

    protected override async Task<Document> FixAsync(Document document, ObjectCreationExpressionSyntax node, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is not CompilationUnitSyntax unit)
        {
            return document;
        }

        if (Guarded(model, node, cancellationToken) is { } guarded)
        {
            unit = unit.ReplaceNode(guarded.If, guarded.Refusal.WithTriviaFrom(guarded.If).WithAdditionalAnnotations(Formatter.Annotation));
            return document.WithSyntaxRoot(WithUsing(model, unit, node.SpanStart, "Refuse", RefuseNamespace));
        }

        var created = ObjectCreationExpression(IdentifierName("RaskValidationException"))
            .WithArgumentList(node.ArgumentList!.WithoutTrivia())
            .WithTriviaFrom(node);
        unit = unit.ReplaceNode(node, created);
        return document.WithSyntaxRoot(WithUsing(model, unit, node.SpanStart, "RaskValidationException", ExceptionNamespace));
    }

    // `if (c) throw new X(m);` (alone, or alone in a block, with no else): the if and the Refuse call it becomes.
    private static (IfStatementSyntax If, ExpressionStatementSyntax Refusal)? Guarded(
        SemanticModel model, ObjectCreationExpressionSyntax node, CancellationToken cancellationToken)
    {
        if (node.Parent is not ThrowStatementSyntax thrown ||
            IfOf(thrown) is not { Else: null } guard ||
            node.ArgumentList is not { Arguments: var arguments } ||
            arguments.Count is < 1 or > 2 ||
            !IsString(model, arguments[0].Expression, cancellationToken))
        {
            return null;
        }

        ExpressionSyntax? field = null;
        if (arguments.Count == 2)
        {
            if (!IsArgumentException(model, node, cancellationToken) || NameOf(arguments[1].Expression) is not { } named)
            {
                return null;
            }

            field = ParenthesizedLambdaExpression(named);
        }

        var (verb, condition) = guard.Condition is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } not
            ? ("Unless", Unparenthesized(not.Operand))
            : ("When", guard.Condition);
        var refuseArguments = field is null
            ? SeparatedList([Argument(condition.WithoutTrivia()), arguments[0].WithoutTrivia()])
            : SeparatedList([Argument(condition.WithoutTrivia()), Argument(field), arguments[0].WithoutTrivia()]);
        var call = InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("Refuse"), IdentifierName(verb)),
            ArgumentList(refuseArguments));
        return (guard, ExpressionStatement(call));
    }

    // Any other throw of one built with its message (and, optionally, the exception it wraps).
    private static bool Rethrown(SemanticModel model, ObjectCreationExpressionSyntax node, CancellationToken cancellationToken) =>
        node.ArgumentList is { Arguments: var arguments } &&
        arguments.Count is 1 or 2 &&
        arguments[0].NameColon is null &&
        IsString(model, arguments[0].Expression, cancellationToken) &&
        (arguments.Count == 1 || IsException(model, arguments[1].Expression, cancellationToken));

    private static IfStatementSyntax? IfOf(ThrowStatementSyntax thrown) =>
        thrown.Parent switch
        {
            IfStatementSyntax direct when direct.Statement == thrown => direct,
            BlockSyntax { Statements.Count: 1, Parent: IfStatementSyntax inBlock } => inBlock,
            _ => null,
        };

    private static ExpressionSyntax Unparenthesized(ExpressionSyntax expression) =>
        expression is ParenthesizedExpressionSyntax { Expression: var inner } ? inner : expression;

    // `nameof(quantity)` → `quantity`, the member the field lambda reads.
    private static IdentifierNameSyntax? NameOf(ExpressionSyntax expression) =>
        expression is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }, ArgumentList.Arguments: { Count: 1 } named } &&
        named[0].Expression is IdentifierNameSyntax member
            ? member.WithoutTrivia()
            : null;

    private static bool IsString(SemanticModel model, ExpressionSyntax expression, CancellationToken cancellationToken) =>
        model.GetTypeInfo(expression, cancellationToken).Type?.SpecialType == SpecialType.System_String;

    private static bool IsException(SemanticModel model, ExpressionSyntax expression, CancellationToken cancellationToken)
    {
        for (var type = model.GetTypeInfo(expression, cancellationToken).Type; type is not null; type = type.BaseType)
        {
            if (type is { Name: "Exception", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } })
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsArgumentException(SemanticModel model, ObjectCreationExpressionSyntax node, CancellationToken cancellationToken) =>
        model.GetTypeInfo(node, cancellationToken).Type is { Name: "ArgumentException" };

    private static CompilationUnitSyntax WithUsing(SemanticModel model, CompilationUnitSyntax unit, int position, string shortName, string ns) =>
        UsingDirectives.Binds(model, position, shortName, ns + "." + shortName) ? unit : UsingDirectives.With(unit, ns);
}
