using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Rask.Core.Forms;

// The shape a member's DataAnnotations attributes give it, for a model that registered none (a hand-written one): its
// lengths, the input it is typed into, the pattern it must match, whether it is edited at all, whether an emptied text stays "" and the control it
// names, and how a list shows it. A generated model or read face registers the same reading, made at compile time,
// so its members are not reflected over.
internal static class AnnotatedShapes
{
    // Per property, once. Bounded by the members of the models an app binds.
    private static readonly ConcurrentDictionary<PropertyInfo, FieldShape?> Read = new();

    /// <summary>What <paramref name="property" />'s attributes say about its field, or null when they say nothing.</summary>
    internal static FieldShape? Of(PropertyInfo property) => Read.GetOrAdd(property, Annotated);

    // What a member holds, by its type: what a generated model registers for a member of no value object.
    internal static FieldKind KindOf(Type type)
    {
        var held = Nullable.GetUnderlyingType(type) ?? type;
        if (held.IsEnum)
        {
            return FieldKind.Choice;
        }

        if (held == typeof(DateOnly))
        {
            return FieldKind.Day;
        }

        if (held == typeof(DateTime) || held == typeof(DateTimeOffset))
        {
            return FieldKind.Moment;
        }

        return Type.GetTypeCode(held) switch
        {
            TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32
                or TypeCode.Int64 or TypeCode.UInt64 => FieldKind.Integer,
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal => FieldKind.Number,
            _ => FieldKind.Text,
        };
    }

    private static FieldShape? Annotated(PropertyInfo property)
    {
        var attributes = property.GetCustomAttributes(inherit: true);
        var (most, least) = Lengths(attributes);
        var input = InputOf(attributes);
        var readOnly = attributes.OfType<EditableAttribute>().Any(static e => !e.AllowEdit);
        var display = attributes.OfType<DisplayFormatAttribute>().FirstOrDefault();
        var keepEmpty = display is { ConvertEmptyStringToNull: false };
        var format = display?.DataFormatString is { Length: > 0 } written ? written : null;
        var nullText = display?.NullDisplayText;
        var listed = !attributes.OfType<ScaffoldColumnAttribute>().Any(static c => !c.Scaffold);
        var hint = HintOf(attributes);
        var pattern = attributes.OfType<RegularExpressionAttribute>().FirstOrDefault()?.Pattern;
        if (most is null && least is null && input is null && pattern is null && !readOnly && !keepEmpty && hint is null && format is null && nullText is null && listed)
        {
            return null;
        }

        return new FieldShape(KindOf(property.PropertyType), MaxLength: most, MinLength: least)
        {
            Input = input,
            Pattern = pattern,
            ReadOnly = readOnly,
            KeepEmpty = keepEmpty,
            Hint = hint,
            Format = format,
            NullText = nullText,
            Listed = listed,
        };
    }

    // The tightest of what the length attributes say: the least of the most, the most of the fewest.
    private static (int? Most, int? Least) Lengths(object[] attributes)
    {
        int? most = null;
        int? least = null;
        foreach (var attribute in attributes)
        {
            var (saysMost, saysLeast) = attribute switch
            {
                StringLengthAttribute length => ((int?)length.MaximumLength, (int?)length.MinimumLength),
                MaxLengthAttribute { Length: > 0 } length => (length.Length, null),
                MinLengthAttribute length => (null, length.Length),
                LengthAttribute length => (length.MaximumLength, length.MinimumLength),
                _ => (null, null),
            };
            most = saysMost is { } m ? Math.Min(most ?? m, m) : most;
            least = saysLeast is { } l and > 0 ? Math.Max(least ?? l, l) : least;
        }

        return (most, least);
    }

    private static InputType? InputOf(object[] attributes)
    {
        if (attributes.OfType<EmailAddressAttribute>().Any())
        {
            return InputType.Email;
        }

        if (attributes.OfType<PhoneAttribute>().Any())
        {
            return InputType.Tel;
        }

        if (attributes.OfType<UrlAttribute>().Any())
        {
            return InputType.Url;
        }

        return attributes.OfType<DataTypeAttribute>().FirstOrDefault()?.DataType switch
        {
            DataType.Password => InputType.Password,
            DataType.EmailAddress => InputType.Email,
            DataType.PhoneNumber => InputType.Tel,
            DataType.Url or DataType.ImageUrl => InputType.Url,
            DataType.Date => InputType.Date,
            DataType.Time => InputType.Time,
            DataType.DateTime => InputType.DatetimeLocal,
            _ => null,
        };
    }

    private static string? HintOf(object[] attributes)
    {
        if (attributes.OfType<UIHintAttribute>().FirstOrDefault()?.UIHint is { Length: > 0 } hint)
        {
            return hint;
        }

        return attributes.OfType<DataTypeAttribute>().Any(static t => t.DataType == DataType.MultilineText)
            ? FieldHints.MultilineText
            : null;
    }
}
