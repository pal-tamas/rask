using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Batteries.Generators;

// What a property's DataAnnotations attributes tell its field, read here so a control bound to a generated model is
// told without reflection — the reading Core makes at run time of a hand-written model (AnnotatedShapes): the lengths
// ([StringLength], [MaxLength], [MinLength], [Length]), the input ([EmailAddress], [Phone], [Url], [DataType]), whether
// it is edited ([Editable(false)]), whether an emptied text stays "" ([DisplayFormat]) and the control it names
// ([UIHint]). On a value object's property the lengths tighten what the value object declares: the input takes the
// tighter of the two.
public sealed partial class ModelInputGenerator
{
    private const string WithinHelper = "__Within";
    private const string Annotations = "System.ComponentModel.DataAnnotations.";
    private const string InputTypeName = "global::Rask.Core.InputType";

    /// <summary>The hints a property's attributes give its field; every member empty where they give none.</summary>
    private sealed record FieldHintsRead(int? MaxLength, int? MinLength, string? Input, bool ReadOnly, bool KeepEmpty, string? Hint)
    {
        internal bool Says => MaxLength is not null || MinLength is not null || Init.Count > 0;

        // The init-only members of a FieldShape, as an object initializer's assignments.
        internal List<string> Init
        {
            get
            {
                var init = new List<string>();
                if (Input is { } input)
                {
                    init.Add("Input = " + InputTypeName + "." + input);
                }

                if (ReadOnly)
                {
                    init.Add("ReadOnly = true");
                }

                if (KeepEmpty)
                {
                    init.Add("KeepEmpty = true");
                }

                if (Hint is { } hint)
                {
                    init.Add("Hint = " + SymbolDisplay.FormatLiteral(hint, quote: true));
                }

                return init;
            }
        }
    }

    // A property's shape: its value object's or its enum's, tightened and told what its attributes say; a property of
    // neither is given one of its type's kind only when its attributes say something.
    private static string? AnnotatedShapeOf(IPropertySymbol property, ITypeSymbol held, ValueObjectShape? valueObject)
    {
        var shaped = ShapeOf(held, valueObject);
        var hints = HintsOf(property.GetAttributes());
        if (!hints.Says)
        {
            return shaped;
        }

        var init = hints.Init;
        var withInit = init.Count > 0 ? " { " + string.Join(", ", init) + " }" : "";
        if (shaped is null)
        {
            var named = new List<string> { FieldKindType + "." + PlainKindOf(property.Type) };
            AddLength(named, "MaxLength", hints.MaxLength);
            AddLength(named, "MinLength", hints.MinLength);
            return "new " + FieldShapeType + "(" + string.Join(", ", named) + ")" + withInit;
        }

        var within = hints.MaxLength is null && hints.MinLength is null
            ? shaped
            : WithinHelper + "(" + shaped + ", " + Literal(hints.MaxLength) + ", " + Literal(hints.MinLength) + ")";
        return init.Count > 0 ? within + " with" + withInit : within;
    }

    // A value object's shape with the lengths its property's attributes add: the tighter of each pair.
    private static void AppendWithin(StringBuilder s, List<ShapedModel> shaped)
    {
        if (!shaped.Exists(static m => m.Fields.Exists(static f => f.Shape.StartsWith(WithinHelper, StringComparison.Ordinal))))
        {
            return;
        }

        s.AppendLine();
        s.AppendLine("    // A shape with the lengths its property's attributes add: the fewer of the most, the more of the fewest.");
        s.Append("    private static ").Append(FieldShapeType).Append(' ').Append(WithinHelper).Append('(').Append(FieldShapeType)
            .AppendLine(" shape, int? maxLength, int? minLength) =>");
        s.AppendLine("        shape with");
        s.AppendLine("        {");
        s.AppendLine("            MaxLength = shape.MaxLength is { } most && maxLength is { } said ? global::System.Math.Min(most, said) : shape.MaxLength ?? maxLength,");
        s.AppendLine("            MinLength = shape.MinLength is { } least && minLength is { } asked ? global::System.Math.Max(least, asked) : shape.MinLength ?? minLength,");
        s.AppendLine("        };");
    }

    private static FieldHintsRead HintsOf(IEnumerable<AttributeData> attributes)
    {
        int? most = null;
        int? least = null;
        string? input = null;
        string? hint = null;
        var readOnly = false;
        var keepEmpty = false;
        foreach (var attribute in attributes)
        {
            var name = attribute.AttributeClass?.ToDisplayString() ?? "";
            if (!name.StartsWith(Annotations, StringComparison.Ordinal))
            {
                continue;
            }

            var local = name.Substring(Annotations.Length);
            var (saysMost, saysLeast) = LengthsOf(local, attribute);
            most = saysMost is { } m ? Math.Min(most ?? m, m) : most;
            least = saysLeast is { } l and > 0 ? Math.Max(least ?? l, l) : least;
            input ??= InputOf(local, attribute);
            hint ??= HintOf(local, attribute);
            readOnly |= string.Equals(local, "EditableAttribute", StringComparison.Ordinal) && Argument(attribute, 0) is false;
            keepEmpty |= string.Equals(local, "DisplayFormatAttribute", StringComparison.Ordinal)
                         && NamedArgument(attribute, "ConvertEmptyStringToNull") is false;
        }

        return new FieldHintsRead(most, least, input, readOnly, keepEmpty, hint);
    }

    private static (int? Most, int? Least) LengthsOf(string attribute, AttributeData data) =>
        attribute switch
        {
            "StringLengthAttribute" => (Argument(data, 0) as int?, NamedArgument(data, "MinimumLength") as int?),
            "MaxLengthAttribute" => (Argument(data, 0) is int most and > 0 ? most : null, null),
            "MinLengthAttribute" => (null, Argument(data, 0) as int?),
            "LengthAttribute" => (Argument(data, 1) as int?, Argument(data, 0) as int?),
            _ => (null, null),
        };

    // DataType's members by their value: Date 2, Time 3, PhoneNumber 5, EmailAddress 10, Password 11, Url 12, ImageUrl 13.
    private static string? InputOf(string attribute, AttributeData data) =>
        attribute switch
        {
            "EmailAddressAttribute" => "Email",
            "PhoneAttribute" => "Tel",
            "UrlAttribute" => "Url",
            "DataTypeAttribute" => Argument(data, 0) switch
            {
                1 => "DatetimeLocal",
                2 => "Date",
                3 => "Time",
                5 => "Tel",
                10 => "Email",
                11 => "Password",
                12 or 13 => "Url",
                _ => null,
            },
            _ => null,
        };

    // [UIHint("Switch")], or DataType.MultilineText (9): the name of the control the member asks for.
    private static string? HintOf(string attribute, AttributeData data) =>
        attribute switch
        {
            "UIHintAttribute" => Argument(data, 0) is string { Length: > 0 } hint ? hint : null,
            "DataTypeAttribute" => Argument(data, 0) is 9 ? "MultilineText" : null,
            _ => null,
        };

    // What a property of no value object and no enum holds, as Core's AnnotatedShapes.KindOf reads it.
    private static string PlainKindOf(ITypeSymbol type)
    {
        var held = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

        return held.SpecialType switch
        {
            SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
                SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 => "Whole",
            SpecialType.System_Decimal or SpecialType.System_Double or SpecialType.System_Single => "Number",
            SpecialType.System_DateTime => "Moment",
            _ => held.ToDisplayString() switch
            {
                "System.DateOnly" => "Day",
                "System.DateTimeOffset" => "Moment",
                _ => "Text",
            },
        };
    }

    private static object? Argument(AttributeData data, int index) =>
        index < data.ConstructorArguments.Length ? data.ConstructorArguments[index].Value : null;

    private static object? NamedArgument(AttributeData data, string name) =>
        data.NamedArguments.FirstOrDefault(n => string.Equals(n.Key, name, StringComparison.Ordinal)).Value.Value;

    private static void AddLength(List<string> named, string name, int? length)
    {
        if (length is { } given)
        {
            named.Add(name + ": " + given.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static string Literal(int? length) => length?.ToString(CultureInfo.InvariantCulture) ?? "null";
}
