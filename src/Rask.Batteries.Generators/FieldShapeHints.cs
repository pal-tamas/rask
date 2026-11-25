using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Batteries.Generators;

// What a property's DataAnnotations attributes tell a control bound to it, or a list that shows it, read at compile time
// so nothing reflects over a generated model or read face — the reading Core makes at run time of a hand-written model
// (AnnotatedShapes): the lengths ([StringLength], [MaxLength], [MinLength], [Length]), the input ([EmailAddress], [Phone],
// [Url], [DataType]), the pattern it must match ([RegularExpression]), whether it is edited ([Editable(false)]), [DisplayFormat]'s empty text, format and null text, the
// control it names ([UIHint]) and whether a list shows it ([ScaffoldColumn]). On a value object's property the lengths
// tighten what the value object declares: the input takes the tighter of the two.
internal static class FieldShapeHints
{
    internal const string WithinHelper = "__Within";

    private const string Annotations = "System.ComponentModel.DataAnnotations.";
    private const string FieldShapeType = "global::Rask.Core.Forms.FieldShape";
    private const string FieldKindType = "global::Rask.Core.Forms.FieldKind";
    private const string InputTypeName = "global::Rask.Core.InputType";

    // The attributes a read face carries from the property it reads: how a list names, writes and shows the column.
    private static readonly string[] ShownBy = ["DisplayAttribute", "DisplayFormatAttribute", "ScaffoldColumnAttribute"];

    /// <summary>
    ///     A property's shape: <paramref name="shaped" /> — its value object's or its enum's — tightened and told what its
    ///     attributes say; for a property of neither, one of its type's kind only when its attributes say something.
    /// </summary>
    internal static string? Annotated(IPropertySymbol property, string? shaped) => Annotated(property, property.Type, shaped);

    /// <summary>
    ///     The shape of <paramref name="member" /> — a property, or an operation's parameter — holding <paramref name="held" />:
    ///     <paramref name="shaped" /> told what its attributes say, or one of the kind of <paramref name="held" /> when only
    ///     they say something (<c>[DataType(DataType.Date)] DateTime? day</c>: a moment drawn as a day).
    /// </summary>
    internal static string? Annotated(ISymbol member, ITypeSymbol held, string? shaped)
    {
        var hints = Read(member.GetAttributes());
        if (!hints.Says)
        {
            return shaped;
        }

        var init = hints.Init;
        var withInit = init.Count > 0 ? " { " + string.Join(", ", init) + " }" : "";
        if (shaped is null)
        {
            var named = new List<string> { FieldKindType + "." + PlainKindOf(held) };
            AddLength(named, "MaxLength", hints.MaxLength);
            AddLength(named, "MinLength", hints.MinLength);
            return "new " + FieldShapeType + "(" + string.Join(", ", named) + ")" + withInit;
        }

        var within = hints.MaxLength is null && hints.MinLength is null
            ? shaped
            : WithinHelper + "(" + shaped + ", " + Literal(hints.MaxLength) + ", " + Literal(hints.MinLength) + ")";
        return init.Count > 0 ? within + " with" + withInit : within;
    }

    /// <summary>Whether any of <paramref name="shapes" /> needs the <c>__Within</c> helper.</summary>
    internal static bool UseWithin(IEnumerable<string> shapes) =>
        shapes.Any(static shape => shape.StartsWith(WithinHelper, StringComparison.Ordinal));

    /// <summary>The <c>__Within</c> helper: a value object's shape with the lengths its property's attributes add.</summary>
    internal static void AppendWithin(StringBuilder s)
    {
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

    /// <summary>
    ///     The <c>[Display]</c>, <c>[DisplayFormat]</c> and <c>[ScaffoldColumn]</c> of <paramref name="property" />, written
    ///     out for the read face's member, so a list's column is named, written and shown as the aggregate's property says.
    /// </summary>
    internal static string ShownAttributes(IPropertySymbol property) =>
        string.Concat(property.GetAttributes()
            .Where(static a => a.AttributeClass is { } type
                               && string.Equals(type.ContainingNamespace?.ToDisplayString() + ".", Annotations, StringComparison.Ordinal)
                               && ShownBy.Contains(type.Name, StringComparer.Ordinal))
            .Select(static a => "[" + a.AttributeClass!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "("
                                + string.Join(", ", a.ConstructorArguments.Select(ValidationAttributeSource.Argument)
                                    .Concat(a.NamedArguments.Select(static n => n.Key + " = " + ValidationAttributeSource.Argument(n.Value))))
                                + ")]"));

    private static Hints Read(IEnumerable<AttributeData> attributes)
    {
        var hints = new Hints();
        foreach (var attribute in attributes)
        {
            var name = attribute.AttributeClass?.ToDisplayString() ?? "";
            if (name.StartsWith(Annotations, StringComparison.Ordinal))
            {
                hints.Add(name.Substring(Annotations.Length), attribute);
            }
        }

        return hints;
    }

    // What a property of no value object and no enum holds, as Core's AnnotatedShapes.KindOf reads it.
    private static string PlainKindOf(ITypeSymbol type) => KnownKindOf(type) ?? "Text";

    /// <summary>
    ///     The <c>FieldKind</c> a plain value is — <c>Integer</c>, <c>Number</c>, <c>Day</c>, <c>Moment</c> or <c>Text</c> —
    ///     or null for one of no kind a control is told about (a <c>bool</c>, an enum, an id).
    /// </summary>
    internal static string? KnownKindOf(ITypeSymbol type)
    {
        var held = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

        return held.SpecialType switch
        {
            SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
                SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 => "Integer",
            SpecialType.System_Decimal or SpecialType.System_Double or SpecialType.System_Single => "Number",
            SpecialType.System_DateTime => "Moment",
            SpecialType.System_String => "Text",
            _ => held.ToDisplayString() switch
            {
                "System.DateOnly" => "Day",
                "System.DateTimeOffset" => "Moment",
                _ => null,
            },
        };
    }

    private static void AddLength(List<string> named, string name, int? length)
    {
        if (length is { } given)
        {
            named.Add(name + ": " + given.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static string Literal(int? length) => length?.ToString(CultureInfo.InvariantCulture) ?? "null";

    /// <summary>The hints a property's attributes give, gathered attribute by attribute.</summary>
    private sealed class Hints
    {
        private int? _most;
        private int? _least;
        private string? _input;
        private string? _pattern;
        private string? _hint;
        private string? _format;
        private string? _nullText;
        private bool _readOnly;
        private bool _keepEmpty;
        private bool _unlisted;

        internal int? MaxLength => _most;

        internal int? MinLength => _least;

        internal bool Says => _most is not null || _least is not null || Init.Count > 0;

        // The init-only members of a FieldShape, as an object initializer's assignments.
        internal List<string> Init
        {
            get
            {
                var init = new List<string>();
                Assign(init, "Input", _input is { } input ? InputTypeName + "." + input : null);
                Assign(init, "Pattern", _pattern is { } pattern ? Quoted(pattern) : null);
                Assign(init, "ReadOnly", _readOnly ? "true" : null);
                Assign(init, "KeepEmpty", _keepEmpty ? "true" : null);
                Assign(init, "Hint", _hint is { } hint ? Quoted(hint) : null);
                Assign(init, "Format", _format is { } format ? Quoted(format) : null);
                Assign(init, "NullText", _nullText is { } nullText ? Quoted(nullText) : null);
                Assign(init, "Listed", _unlisted ? "false" : null);
                return init;
            }
        }

        internal void Add(string attribute, AttributeData data)
        {
            var (most, least) = LengthsOf(attribute, data);
            _most = most is { } m ? Math.Min(_most ?? m, m) : _most;
            _least = least is { } l and > 0 ? Math.Max(_least ?? l, l) : _least;
            _input ??= InputOf(attribute, data);
            _hint ??= HintOf(attribute, data);
            _pattern ??= string.Equals(attribute, "RegularExpressionAttribute", StringComparison.Ordinal) ? Argument(data, 0) as string : null;
            _readOnly |= string.Equals(attribute, "EditableAttribute", StringComparison.Ordinal) && Argument(data, 0) is false;
            _unlisted |= string.Equals(attribute, "ScaffoldColumnAttribute", StringComparison.Ordinal) && Argument(data, 0) is false;
            if (string.Equals(attribute, "DisplayFormatAttribute", StringComparison.Ordinal))
            {
                _keepEmpty |= NamedArgument(data, "ConvertEmptyStringToNull") is false;
                _format ??= NamedArgument(data, "DataFormatString") as string is { Length: > 0 } format ? format : null;
                _nullText ??= NamedArgument(data, "NullDisplayText") as string;
            }
        }

        private static object? Argument(AttributeData data, int index) =>
            index < data.ConstructorArguments.Length ? data.ConstructorArguments[index].Value : null;

        private static object? NamedArgument(AttributeData data, string name) =>
            data.NamedArguments.FirstOrDefault(n => string.Equals(n.Key, name, StringComparison.Ordinal)).Value.Value;

        private static string Quoted(string text) => SymbolDisplay.FormatLiteral(text, quote: true);

        private static void Assign(List<string> init, string member, string? value)
        {
            if (value is not null)
            {
                init.Add(member + " = " + value);
            }
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

        // DataType's members by their value: DateTime 1, Date 2, Time 3, PhoneNumber 5, EmailAddress 10, Password 11, Url 12,
        // ImageUrl 13.
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
    }
}
