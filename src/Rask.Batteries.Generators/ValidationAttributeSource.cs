using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Batteries.Generators;

/// <summary>
///     A DataAnnotations attribute as C# the server runs: what a generated model's write and the save of an aggregate
///     both build their checks from, so the two ask exactly the same thing.
/// </summary>
internal static class ValidationAttributeSource
{
    private const string ValidationAttribute = "System.ComponentModel.DataAnnotations.ValidationAttribute";
    private const string AsyncValidationAttribute = "System.ComponentModel.DataAnnotations.AsyncValidationAttribute";
    private const string DisplayAttribute = "System.ComponentModel.DataAnnotations.DisplayAttribute";

    /// <summary>
    ///     Whether the attribute validates: <c>[Display]</c>, <c>[UIHint]</c> and the other DataAnnotations that describe
    ///     a field are copied to a form but check nothing.
    /// </summary>
    public static bool IsValidation(AttributeData attribute) => DerivesFrom(attribute, ValidationAttribute);

    /// <summary>Whether the attribute is one .NET awaits (<c>AsyncValidationAttribute</c>), never asked synchronously.</summary>
    public static bool IsAsync(AttributeData attribute) => DerivesFrom(attribute, AsyncValidationAttribute);

    /// <summary>The attribute as an object the server validates with — named arguments become an initializer.</summary>
    public static string Construction(AttributeData attribute)
    {
        var named = attribute.NamedArguments.Select(static n => n.Key + " = " + Argument(n.Value)).ToList();
        return "new " + attribute.AttributeClass!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
               "(" + string.Join(", ", attribute.ConstructorArguments.Select(Argument)) + ")" +
               (named.Count == 0 ? "" : " { " + string.Join(", ", named) + " }");
    }

    /// <summary>
    ///     What a failed check calls the member, as C# the server runs: <c>[Display]</c>'s name — through its resources,
    ///     at run time, where it names a <c>ResourceType</c> — else the member's own.
    /// </summary>
    public static string NameOf(ISymbol member)
    {
        var attributes = member.GetAttributes();
        var fallback = SymbolDisplay.FormatLiteral(member.Name, quote: true);

        if (attributes.FirstOrDefault(static a => a.AttributeClass?.ToDisplayString() is DisplayAttribute) is not { } display)
        {
            return fallback;
        }

        if (display.NamedArguments.Any(static n => n.Key is "ResourceType" && !n.Value.IsNull))
        {
            return "(" + Construction(display) + ").GetName() ?? " + fallback;
        }

        return display.NamedArguments.FirstOrDefault(static n => n.Key is "Name").Value is { Value: string { Length: > 0 } } name
            ? name.ToCSharpString()
            : fallback;
    }

    /// <summary>
    ///     One argument as C#. <c>TypedConstant.ToCSharpString</c> renders an array as a bare <c>{ a, b }</c>, which is not
    ///     an expression and so not an attribute argument: <c>[AllowedValues("draft", "live")]</c> was copied as a syntax
    ///     error. An array is written as the array creation a params argument is shorthand for.
    /// </summary>
    public static string Argument(TypedConstant constant) =>
        constant.Kind == TypedConstantKind.Array && !constant.IsNull
            ? "new " + constant.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + " { " +
              string.Join(", ", constant.Values.Select(Argument)) + " }"
            : constant.ToCSharpString();

    private static bool DerivesFrom(AttributeData attribute, string baseName)
    {
        for (var current = attribute.AttributeClass?.BaseType; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToDisplayString(), baseName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
