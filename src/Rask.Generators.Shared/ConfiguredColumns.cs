using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.Shared;

/// <summary>
///     Reads the column patterns an entity's static <c>Configure</c> gives its value objects of several members —
///     <c>b.Columns(n =&gt; n.Km, "{Member}Km")</c> — from its source, so the read face names its members as the columns
///     are named.
/// </summary>
/// <remarks>
///     Only a call bound to Rask.Data's <c>Columns</c>, whose member is a property of the entity and whose pattern is a
///     constant. Like <see cref="ConfiguredRelationships" />, it sees what is written in that method — not a helper it
///     calls.
/// </remarks>
internal static class ConfiguredColumns
{
    /// <summary>The placeholder a pattern names each member of the value object with.</summary>
    public const string Member = "{Member}";

    private const string Extensions = "Rask.Data.ValueObjectColumnsBuilderExtensions";

    /// <summary>Each property <paramref name="entity" />'s <c>Configure</c> names the columns of, with its pattern.</summary>
    public static IReadOnlyList<ColumnPattern> Of(INamedTypeSymbol entity, Compilation compilation, CancellationToken cancellationToken)
    {
        var found = new List<ColumnPattern>();

        foreach (var reference in entity.GetMembers("Configure").OfType<IMethodSymbol>()
                     .Where(static m => m.IsStatic)
                     .SelectMany(static m => m.DeclaringSyntaxReferences))
        {
            var syntax = reference.GetSyntax(cancellationToken);
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);

            foreach (var call in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (call is { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Columns" }, ArgumentList.Arguments.Count: 2 } &&
                    Read(call, model, cancellationToken) is { } pattern)
                {
                    found.Add(pattern);
                }
            }
        }

        return found;
    }

    /// <summary>The column name <paramref name="pattern" /> gives the value object's member <paramref name="name" />.</summary>
    public static string Apply(string pattern, string name) => pattern.Replace(Member, name);

    private static ColumnPattern? Read(InvocationExpressionSyntax call, SemanticModel model, CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(call, cancellationToken).Symbol is not IMethodSymbol method ||
            !string.Equals((method.ReducedFrom ?? method).ContainingType.ToDisplayString(), Extensions, StringComparison.Ordinal))
        {
            return null;
        }

        var arguments = call.ArgumentList.Arguments;
        if (arguments[0].Expression is not SimpleLambdaExpressionSyntax { ExpressionBody: MemberAccessExpressionSyntax access } lambda ||
            access.Expression is not IdentifierNameSyntax parameter ||
            !string.Equals(parameter.Identifier.ValueText, lambda.Parameter.Identifier.ValueText, StringComparison.Ordinal) ||
            model.GetConstantValue(arguments[1].Expression, cancellationToken) is not { HasValue: true, Value: string pattern } ||
            pattern.IndexOf(Member, StringComparison.Ordinal) < 0)
        {
            return null;
        }

        return new ColumnPattern(access.Name.Identifier.ValueText, pattern);
    }
}
