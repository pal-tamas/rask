using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Api.Generators;

/// <summary>
///     Whether a <c>Map*</c> call was taken out of the app's API description — on the endpoint itself,
///     or on the group (or a group above it) that it is mapped on.
/// </summary>
/// <remarks>
///     ASP.NET flows a group's conventions to every endpoint in it, so
///     <c>var site = app.MapGroup("").ExcludeFromDescription(); site.MapGet(...)</c> keeps that endpoint
///     out of OpenAPI. A group is traced only through what this file can see — a fluent chain, a local or
///     a field initialised from one, and calls made on that local or field here. A group that arrives as a
///     parameter cannot be traced, so its endpoints are treated as described (and reported as before).
///     Staying inside one syntax tree is also what keeps the incremental generator honest: it re-reads a
///     call only when its own file changes.
/// </remarks>
internal static class DescriptionExclusion
{
    // A cycle between field initialisers is legal C#; this bounds the walk instead of tracking a set.
    private const int MaxDepth = 16;

    /// <summary>Whether the endpoint mapped by <paramref name="invocation" /> is excluded.</summary>
    public static bool IsExcluded(SemanticModel model, InvocationExpressionSyntax invocation) =>
        ChainExcludes(invocation) ||
        (invocation.Expression is MemberAccessExpressionSyntax member && ReceiverExcludes(model, member.Expression, 0));

    // .ExcludeFromDescription() anywhere in the chain hanging off this call.
    private static bool ChainExcludes(InvocationExpressionSyntax invocation)
    {
        for (SyntaxNode? node = invocation.Parent;
             node is InvocationExpressionSyntax or MemberAccessExpressionSyntax;
             node = node.Parent)
        {
            if (node is InvocationExpressionSyntax call && IsExclusion(call))
            {
                return true;
            }
        }

        return false;
    }

    // Walks the receiver down to where it came from: through every call on it (a MapGroup's own receiver
    // is the parent group), and into the declaration of a local or field it was read from.
    private static bool ReceiverExcludes(SemanticModel model, ExpressionSyntax receiver, int depth)
    {
        if (depth > MaxDepth)
        {
            return false;
        }

        switch (receiver)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return ReceiverExcludes(model, parenthesized.Expression, depth + 1);

            case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } call:
                return IsExclusion(call) || ReceiverExcludes(model, member.Expression, depth + 1);

            case IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }:
                return model.GetSymbolInfo(receiver).Symbol is ILocalSymbol or IFieldSymbol
                    && SymbolExcludes(model, model.GetSymbolInfo(receiver).Symbol!, depth);

            default:
                return false;
        }
    }

    private static bool SymbolExcludes(SemanticModel model, ISymbol symbol, int depth)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree != model.SyntaxTree ||
                reference.GetSyntax() is not VariableDeclaratorSyntax declarator)
            {
                continue;
            }

            if (declarator.Initializer is { } initializer &&
                ReceiverExcludes(model, initializer.Value, depth + 1))
            {
                return true;
            }

            // `site.ExcludeFromDescription();` as a statement of its own: a convention added to the group
            // after it was declared still reaches every endpoint in it.
            var scope = symbol is ILocalSymbol
                ? declarator.Ancestors().FirstOrDefault(a => a is BlockSyntax or CompilationUnitSyntax)
                : declarator.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();

            if (scope is not null && IsExcludedWithin(model, scope, symbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExcludedWithin(SemanticModel model, SyntaxNode scope, ISymbol symbol) =>
        scope.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call =>
            IsExclusion(call) &&
            call.Expression is MemberAccessExpressionSyntax member &&
            SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(member.Expression).Symbol, symbol));

    // .ExcludeFromDescription(), or the metadata it stands for: .WithMetadata(new ExcludeFromDescriptionAttribute()).
    private static bool IsExclusion(InvocationExpressionSyntax call) =>
        call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name } &&
        (string.Equals(name, "ExcludeFromDescription", StringComparison.Ordinal) ||
         (string.Equals(name, "WithMetadata", StringComparison.Ordinal) &&
          call.ArgumentList.Arguments.Any(argument => IsExclusionAttribute(argument.Expression))));

    private static bool IsExclusionAttribute(ExpressionSyntax expression) =>
        expression is ObjectCreationExpressionSyntax { Type: var type } &&
        (type is QualifiedNameSyntax qualified ? qualified.Right : type) is IdentifierNameSyntax
        {
            Identifier.ValueText: "ExcludeFromDescriptionAttribute" or "ExcludeFromDescription",
        };
}
