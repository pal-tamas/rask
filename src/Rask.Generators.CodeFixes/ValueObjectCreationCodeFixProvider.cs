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
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Rask.Generators.CodeFixes;

/// <summary>
///     RASK127 — <c>new KmRange(a, b)</c> becomes <c>KmRange.Create(a, b)</c>, and <c>km with { End = 5 }</c> becomes
///     <c>KmRange.Create(km.Start, 5)</c>: the generated door that checks the value object's rules.
/// </summary>
/// <remarks>
///     A <c>new</c> with an object initializer gets no fix. A <c>with</c> gets one only when every member it sets is a
///     positional one, every positional member can be read back from the copied value, and the copied value is a
///     name (<c>km</c>, <c>this.Km</c>) — so reading it once per member changes nothing.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ValueObjectCreationCodeFixProvider))]
[Shared]
public sealed class ValueObjectCreationCodeFixProvider : RaskCodeFixProvider<ExpressionSyntax>
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["RASK127"];

    protected override string Title => "Make it with Create";

    protected override string EquivalenceKey => "RASK127_UseCreate";

    protected override async Task<bool> CanFixAsync(CodeFixContext context, ExpressionSyntax node) =>
        await CreateCallAsync(context.Document, node, context.CancellationToken).ConfigureAwait(false) is not null;

    protected override async Task<Document> FixAsync(Document document, ExpressionSyntax node, CancellationToken cancellationToken) =>
        await CreateCallAsync(document, node, cancellationToken).ConfigureAwait(false) is { } call
            ? await ReplaceNodeAsync(document, node, call.WithTriviaFrom(node), cancellationToken).ConfigureAwait(false)
            : document;

    private static async Task<ExpressionSyntax?> CreateCallAsync(Document document, ExpressionSyntax node, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            return null;
        }

        return node switch
        {
            BaseObjectCreationExpressionSyntax { Initializer: null, ArgumentList: { } arguments } made =>
                TypeOf(model, made) is { } type ? Create(type, arguments) : null,
            WithExpressionSyntax copy => FromCopy(model, copy, cancellationToken),
            _ => null,
        };
    }

    // `new KmRange(…)` names its type; `new(…)` takes it from the target, written as the code there would name it.
    private static TypeSyntax? TypeOf(SemanticModel model, BaseObjectCreationExpressionSyntax made) =>
        made is ObjectCreationExpressionSyntax { Type: { } named }
            ? named.WithoutTrivia()
            : Named(model, model.GetTypeInfo(made).Type, made.SpanStart);

    private static TypeSyntax? Named(SemanticModel model, ITypeSymbol? type, int position) =>
        type is null ? null : ParseTypeName(type.ToMinimalDisplayString(model, position));

    private static InvocationExpressionSyntax Create(TypeSyntax type, ArgumentListSyntax arguments) =>
        InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, type, IdentifierName("Create")),
            arguments.WithoutTrivia());

    // `km with { End = 5 }` → `KmRange.Create(km.Start, 5)`: each positional member, set by the `with` or read back.
    private static InvocationExpressionSyntax? FromCopy(SemanticModel model, WithExpressionSyntax copy, CancellationToken cancellationToken)
    {
        if (!IsName(copy.Expression) ||
            model.GetTypeInfo(copy.Expression, cancellationToken).Type is not INamedTypeSymbol type ||
            Positional(type) is not { Count: > 0 } members)
        {
            return null;
        }

        var set = new Dictionary<string, ExpressionSyntax>(System.StringComparer.Ordinal);
        foreach (var assignment in copy.Initializer.Expressions)
        {
            if (assignment is not AssignmentExpressionSyntax { Left: IdentifierNameSyntax { Identifier.ValueText: var name } } given ||
                !members.Contains(name, System.StringComparer.Ordinal))
            {
                return null;
            }

            set[name] = given.Right.WithoutTrivia();
        }

        if (members.Exists(name => !set.ContainsKey(name) && !Readable(type, name)))
        {
            return null;
        }

        var arguments = members.Select(name => Argument(
            set.TryGetValue(name, out var value)
                ? value
                : MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, copy.Expression.WithoutTrivia(), IdentifierName(name))));

        return Named(model, type, copy.SpanStart) is { } typeName
            ? Create(typeName, ArgumentList(SeparatedList(arguments)))
            : null;
    }

    // Read again for each member, so only a name may be: `km`, `this.Km`, `order.Km`.
    private static bool IsName(ExpressionSyntax expression) =>
        expression switch
        {
            IdentifierNameSyntax or ThisExpressionSyntax => true,
            MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression } access => IsName(access.Expression),
            _ => false,
        };

    // The primary constructor's parameters, in order: what Create takes.
    private static List<string>? Positional(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences
            .Select(static r => r.GetSyntax())
            .OfType<RecordDeclarationSyntax>()
            .FirstOrDefault(static r => r.ParameterList is not null)?
            .ParameterList!.Parameters.Select(static p => p.Identifier.ValueText).ToList();

    private static bool Readable(INamedTypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(name).OfType<IPropertySymbol>().Any(static p => p.GetMethod is not null && !p.IsStatic))
            {
                return true;
            }
        }

        return false;
    }
}
