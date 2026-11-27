using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Batteries.Generators;

/// <summary>
///     Who produces an entity's key, as Rask.Data's key convention decides it and the entity's own <c>Configure</c>
///     changes it — the one reading the generated <c>Create</c> (<see cref="ModelInputGenerator" />) and RASK145 share.
/// </summary>
/// <remarks>
///     Read from the source, as the unique rules are, because the writes are generated before any model is built. So it
///     sees the one chain that starts at <c>Property(…Id)</c> in a static <c>Configure</c> on the entity — any of its
///     partial declarations — and nothing else: not a helper that method calls, an <c>IEntityTypeConfiguration</c>,
///     <c>OnModelCreating</c>, or a builder kept in a variable first.
/// </remarks>
internal static class KeyConfiguration
{
    /// <summary>
    ///     Whether the key is an integer the convention leaves to the store's identity: an <c>int</c>, a <c>long</c> (any
    ///     integral type), or a strongly-typed id over an <c>int</c> or a <c>long</c>.
    /// </summary>
    public static bool IsInteger(ITypeSymbol idType)
    {
        switch (idType.SpecialType)
        {
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                return true;
        }

        return ModelRegistryGenerator.StronglyTypedId.For(idType) is { Problem: null, ValueTypeName: "int" or "long" };
    }

    /// <summary>
    ///     Whether the store produces <paramref name="entity" />'s key on insert, so it is the default (<c>0</c>) until the
    ///     first save: an integer key the entity did not configure <c>ValueGeneratedNever()</c>, or any key it configured
    ///     <c>ValueGeneratedOnAdd()</c>.
    /// </summary>
    public static bool IsStoreGenerated(INamedTypeSymbol entity, ITypeSymbol idType, CancellationToken cancellationToken) =>
        Configures(entity, "ValueGeneratedOnAdd", cancellationToken) ||
        (IsInteger(idType) && !ConfiguresNeverGenerated(entity, cancellationToken));

    /// <summary>
    ///     Whether the entity's own <c>Configure</c> says its key is assigned by the application:
    ///     <c>builder.Property(x =&gt; x.Id).ValueGeneratedNever()</c>.
    /// </summary>
    public static bool ConfiguresNeverGenerated(INamedTypeSymbol entity, CancellationToken cancellationToken) =>
        Configures(entity, "ValueGeneratedNever", cancellationToken);

    // builder.Property(x => x.Id).<configuration>() in a static Configure of the entity.
    private static bool Configures(INamedTypeSymbol entity, string configuration, CancellationToken cancellationToken) =>
        entity.GetMembers("Configure").OfType<IMethodSymbol>()
            .Where(static method => method.IsStatic)
            .SelectMany(static method => method.DeclaringSyntaxReferences)
            .SelectMany(reference => reference.GetSyntax(cancellationToken).DescendantNodes())
            .OfType<InvocationExpressionSyntax>()
            .Any(call =>
                call.Expression is MemberAccessExpressionSyntax access &&
                string.Equals(access.Name.Identifier.ValueText, configuration, StringComparison.Ordinal) &&
                IsOnTheKey(access.Expression));

    // Walks the chain back from the configuration to the Property(…) call it configures.
    private static bool IsOnTheKey(ExpressionSyntax receiver)
    {
        while (receiver is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } call)
        {
            if (string.Equals(access.Name.Identifier.ValueText, "Property", StringComparison.Ordinal))
            {
                return call.ArgumentList.Arguments.Count > 0 && NamesTheKey(call.ArgumentList.Arguments[0].Expression);
            }

            receiver = access.Expression;
        }

        return false;
    }

    // x => x.Id, "Id" or nameof(Order.Id).
    private static bool NamesTheKey(ExpressionSyntax argument)
    {
        var named = argument switch
        {
            LambdaExpressionSyntax { Body: MemberAccessExpressionSyntax member } => member.Name.Identifier.ValueText,
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
            InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }, ArgumentList.Arguments: { Count: 1 } of } =>
                of[0].Expression switch
                {
                    MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                    IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                    _ => null,
                },
            _ => null,
        };

        return string.Equals(named, "Id", StringComparison.Ordinal);
    }
}
