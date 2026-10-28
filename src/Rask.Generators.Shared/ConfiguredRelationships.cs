using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rask.Batteries.Generators;

namespace Rask.Generators.Shared;

/// <summary>Reads the relationships an entity's static <c>Configure</c> declares, from its source.</summary>
/// <remarks>
///     Every <c>HasForeignKey</c> in the method, bound to EF Core's builder so the two ends come from its type
///     arguments: <c>HasOne&lt;P&gt;().WithMany()</c> makes the entity the dependent, <c>HasMany&lt;D&gt;().WithOne()</c>
///     makes <c>D</c> one, and a one-to-one names its dependent itself. It sees what is written in that method —
///     not a helper it calls, an <c>IEntityTypeConfiguration</c>, or <c>OnModelCreating</c>.
/// </remarks>
internal static class ConfiguredRelationships
{
    private const string Builders = "Microsoft.EntityFrameworkCore.Metadata.Builders";

    /// <summary>The relationships <paramref name="entity" />'s <c>Configure</c> declares, in source order.</summary>
    public static IReadOnlyList<ReadRelationship> Of(
        INamedTypeSymbol entity, Compilation compilation, CancellationToken cancellationToken)
    {
        var found = new List<ReadRelationship>();

        foreach (var reference in entity.GetMembers("Configure").OfType<IMethodSymbol>()
                     .Where(static m => m.IsStatic)
                     .SelectMany(static m => m.DeclaringSyntaxReferences))
        {
            var syntax = reference.GetSyntax(cancellationToken);
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);

            foreach (var call in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "HasForeignKey" } &&
                    Read(call, model, cancellationToken) is { } relationship)
                {
                    found.Add(relationship);
                }
            }
        }

        return found;
    }

    private static ReadRelationship? Read(InvocationExpressionSyntax call, SemanticModel model, CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(call, cancellationToken).Symbol is not IMethodSymbol method ||
            Ends(method) is not { } ends ||
            ForeignKeys(call) is not { Count: > 0 } keys)
        {
            return null;
        }

        return new ReadRelationship(
            ends.Dependent.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ends.Principal.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            new EquatableArray<string>(keys),
            JoinsAnotherKey(call));
    }

    // ReferenceCollectionBuilder<TPrincipal, TDependent>, or a one-to-one whose HasForeignKey<TDependent> says which end holds it.
    private static (ITypeSymbol Principal, ITypeSymbol Dependent)? Ends(IMethodSymbol method)
    {
        var builder = method.ContainingType;
        if (!string.Equals(builder.ContainingNamespace?.ToDisplayString(), Builders, StringComparison.Ordinal) ||
            builder.TypeArguments.Length != 2)
        {
            return null;
        }

        if (string.Equals(builder.Name, "ReferenceCollectionBuilder", StringComparison.Ordinal))
        {
            return (builder.TypeArguments[0], builder.TypeArguments[1]);
        }

        if (string.Equals(builder.Name, "ReferenceReferenceBuilder", StringComparison.Ordinal) &&
            method.TypeArguments.Length == 1)
        {
            var dependent = method.TypeArguments[0];
            var principal = SymbolEqualityComparer.Default.Equals(dependent, builder.TypeArguments[0])
                ? builder.TypeArguments[1]
                : builder.TypeArguments[0];
            return (principal, dependent);
        }

        return null;
    }

    // c => c.ProviderId, c => new { c.A, c.B }, or the names as strings; anything else is not read.
    private static List<string>? ForeignKeys(InvocationExpressionSyntax call)
    {
        var arguments = call.ArgumentList.Arguments;

        if (arguments.Count == 1 && arguments[0].Expression is LambdaExpressionSyntax { ExpressionBody: { } body })
        {
            return body switch
            {
                MemberAccessExpressionSyntax access => [access.Name.Identifier.ValueText],
                AnonymousObjectCreationExpressionSyntax members => Members(members),
                _ => null,
            };
        }

        var names = arguments
            .Select(static a => a.Expression is LiteralExpressionSyntax literal ? literal.Token.ValueText : null)
            .ToList();
        return names.Contains(null) ? null : [.. names.OfType<string>()];
    }

    private static List<string>? Members(AnonymousObjectCreationExpressionSyntax members)
    {
        var names = members.Initializers
            .Select(static i => i.Expression is MemberAccessExpressionSyntax access ? access.Name.Identifier.ValueText : null)
            .ToList();
        return names.Contains(null) ? null : [.. names.OfType<string>()];
    }

    // HasPrincipalKey anywhere in the same chain, before or after the foreign key.
    private static bool JoinsAnotherKey(InvocationExpressionSyntax call)
    {
        SyntaxNode chain = call;
        while (chain.Parent is MemberAccessExpressionSyntax or InvocationExpressionSyntax)
        {
            chain = chain.Parent;
        }

        return chain.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>()
            .Any(static a => string.Equals(a.Name.Identifier.ValueText, "HasPrincipalKey", StringComparison.Ordinal));
    }
}
