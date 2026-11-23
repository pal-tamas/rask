using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Rask.Generators.CodeFixes;

/// <summary>
///     RASK127 — <c>new DriverName(value)</c> becomes <c>DriverName.Create(value)</c>, the generated door that checks
///     the value object's rules.
/// </summary>
/// <remarks>
///     Offered for a construction with exactly one argument and no initializer: that argument is the value, and
///     <c>Create</c> takes it. A <c>with</c> has no one mechanical rewrite and gets no fix.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ValueObjectCreationCodeFixProvider))]
[Shared]
public sealed class ValueObjectCreationCodeFixProvider : RaskCodeFixProvider<BaseObjectCreationExpressionSyntax>
{
    // Mirrors ValueObjectCreationAnalyzer.NoFixProperty in Rask.Batteries.Generators, which this assembly cannot reference.
    private const string NoFixProperty = "RaskNoFix";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK127"];

    protected override string Title => "Make it with Create";

    protected override string EquivalenceKey => "RASK127_UseCreate";

    protected override Task<bool> CanFixAsync(CodeFixContext context, BaseObjectCreationExpressionSyntax node) =>
        Task.FromResult(
            !context.Diagnostics[0].Properties.ContainsKey(NoFixProperty) &&
            node is { Initializer: null, ArgumentList.Arguments.Count: 1 });

    protected override async Task<Document> FixAsync(
        Document document, BaseObjectCreationExpressionSyntax node, CancellationToken cancellationToken)
    {
        var type = node is ObjectCreationExpressionSyntax { Type: { } named }
            ? named.WithoutTrivia()
            : await ImplicitType(document, node, cancellationToken).ConfigureAwait(false);

        if (type is null)
        {
            return document;
        }

        var call = InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, type, IdentifierName("Create")),
                node.ArgumentList!)
            .WithTriviaFrom(node);

        return await ReplaceNodeAsync(document, node, call, cancellationToken).ConfigureAwait(false);
    }

    // `new("Ada")`: the type it makes, as the code there would name it.
    private static async Task<TypeSyntax?> ImplicitType(
        Document document, BaseObjectCreationExpressionSyntax node, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        return model?.GetTypeInfo(node, cancellationToken).Type is { } made
            ? ParseTypeName(made.ToMinimalDisplayString(model, node.SpanStart))
            : null;
    }
}
