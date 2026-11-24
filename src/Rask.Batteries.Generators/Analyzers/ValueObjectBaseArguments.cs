using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     The literal arguments a value object hands its <c>Text</c> or <c>Number</c> base, by parameter name:
///     <c>Text(Value, MaxLength: 100)</c> reads as <c>MaxLength = 100</c>.
/// </summary>
/// <remarks>
///     Read from the declaration as written, so a length computed at run time is not one — every check built on it
///     compares constants only, and is never a guess.
/// </remarks>
internal static class ValueObjectBaseArguments
{
    // The parameters of each base's constructor, in order, for an argument given by position.
    private static readonly string[] TextParameters = ["Value", "MaxLength", "MinLength", "Pattern", "Message", "Trim"];
    private static readonly string[] NumberParameters = ["Value", "Min", "Max", "Scale", "Precision"];

    /// <summary>Whether the value object is declared from <c>Text</c>.</summary>
    public static bool IsText(INamedTypeSymbol valueObject) =>
        string.Equals(AggregateShape.ValueBaseOf(valueObject), "Text", System.StringComparison.Ordinal);

    /// <summary>The int literals the value object's base call is given, by parameter name; null when it has no base call.</summary>
    public static Dictionary<string, int>? Of(INamedTypeSymbol valueObject)
    {
        var names = IsText(valueObject) ? TextParameters : NumberParameters;
        var arguments = valueObject.DeclaringSyntaxReferences
            .Select(static r => r.GetSyntax())
            .OfType<RecordDeclarationSyntax>()
            .SelectMany(static r => r.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>() ?? [])
            .Select(static b => b.ArgumentList)
            .FirstOrDefault();

        if (arguments is null)
        {
            return null;
        }

        var declared = new Dictionary<string, int>(System.StringComparer.Ordinal);
        for (var i = 0; i < arguments.Arguments.Count; i++)
        {
            var argument = arguments.Arguments[i];
            var name = argument.NameColon?.Name.Identifier.ValueText ?? (i < names.Length ? names[i] : null);
            if (name is not null && argument.Expression is LiteralExpressionSyntax { Token.Value: int value })
            {
                declared[name] = value;
            }
        }

        return declared;
    }
}
