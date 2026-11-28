using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>
///     Which value objects System.Text.Json writes as the one value they hold — <c>"Ada"</c>, not <c>{"Value":"Ada"}</c> —
///     through the converter the value object generator nests in each (<c>__Json</c>), and so which ones every wire that
///     must agree with a System.Text.Json reader carries bare (the API client's codec, <c>WireShape</c>).
/// </summary>
/// <remarks>
///     A record of one value declared from Text (or one of its kinds), Number, Integer, Day, Moment, or Value over one
///     own member, whose value is a scalar or an enum of the wire. Not a password, which is input only and travels as it
///     always has; not a generic record, nor one over a nullable member (its null would read as the record's own); and not
///     one that names a [JsonConverter] of its own.
/// </remarks>
internal static class ValueObjectJson
{
    /// <summary>The name of the converter the value object generator nests in each record it writes bare.</summary>
    public const string ConverterName = "__Json";

    private const string ConverterAttribute = "System.Text.Json.Serialization.JsonConverterAttribute";

    /// <summary>How <paramref name="type" /> is written bare, or null when System.Text.Json writes it as an object.</summary>
    /// <param name="type">The type.</param>
    /// <returns>Its bare form, or null.</returns>
    public static BareJson? Of(INamedTypeSymbol type)
    {
        if (AggregateShape.IsPassword(type) || DeclaresOwnConverter(type) || Held(type) is not ({ } held, { } parameter) ||
            IsNullable(held.Type) || IsNullable(parameter.Type))
        {
            return null;
        }

        var heldType = held.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (string.Equals(AggregateShape.DeclaredValueBaseOf(type), "Moment", StringComparison.Ordinal))
        {
            return held.Type.SpecialType == SpecialType.System_DateTime ? new BareJson(held.Name, heldType, "", "", Moment: true) : null;
        }

        if (held.Type.TypeKind == TypeKind.Enum)
        {
            return new BareJson(
                held.Name, heldType, "(" + heldType + ")global::Rask.Wire.WireJson.ReadInt64(ref reader, property)",
                "writer.WriteNumberValue((long){0})", Moment: false);
        }

        if (WireScalars.Of(held.Type) is not { } scalar)
        {
            return null;
        }

        var read = scalar.Read + "(ref reader, property)";
        if (held.Type.IsReferenceType)
        {
            read += " ?? throw new global::System.Text.Json.JsonException(\"'\" + property + \"' holds no value.\")";
        }

        return new BareJson(held.Name, heldType, read, scalar.Write, Moment: false);
    }

    /// <summary>
    ///     The member a value object of one value holds it in, fed by its one public constructor of one parameter — or
    ///     null for any other type, a generic record included.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The member, or null.</returns>
    public static IPropertySymbol? HeldOf(INamedTypeSymbol type) => Held(type)?.Member;

    private static (IPropertySymbol Member, IParameterSymbol Parameter)? Held(INamedTypeSymbol type)
    {
        if (type.IsGenericType || AggregateShape.ValueBaseOf(type) is null)
        {
            return null;
        }

        var constructors = type.InstanceConstructors
            .Where(static c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length > 0)
            .Take(2)
            .ToList();
        if (constructors.Count != 1 || constructors[0].Parameters.Length != 1)
        {
            return null;
        }

        var parameter = constructors[0].Parameters[0];
        var stored = AggregateShape.StoredProperties(type).Take(2).ToList();
        return stored.Count == 1 && SymbolEqualityComparer.Default.Equals(stored[0].Type, parameter.Type)
            ? (stored[0], parameter)
            : null;
    }

    /// <summary>Whether <paramref name="type" /> names a [JsonConverter] other than the one the generator nests in it.</summary>
    /// <param name="type">The type.</param>
    /// <returns>Whether it chose its own converter.</returns>
    public static bool DeclaresOwnConverter(INamedTypeSymbol type) =>
        type.GetAttributes().Any(a =>
            string.Equals(a.AttributeClass?.ToDisplayString(), ConverterAttribute, StringComparison.Ordinal) &&
            !(a.ConstructorArguments.Length == 1 &&
              a.ConstructorArguments[0].Value is INamedTypeSymbol { Name: ConverterName } converter &&
              SymbolEqualityComparer.Default.Equals(converter.ContainingType, type)));

    private static bool IsNullable(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated ||
        type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
}
