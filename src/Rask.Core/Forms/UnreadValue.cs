using System.Globalization;
using Rask.Core.Globalization;

namespace Rask.Core.Forms;

// What a bound field could not read of what was typed into it, as the sentence the form says under it. The
// bound member's type chooses: a whole number, a number, a date, a time. A member of another type — text
// behind a number input — is answered by the kind of input the browser named.
internal static class UnreadValue
{
    /// <summary>The sentence for text the browser held back: <paramref name="input" /> is the input's type.</summary>
    internal static RaskString? HeldBack(Type member, string input) => OfType(member) ?? input switch
    {
        "number" or "range" => RaskString.ValidationNumber,
        "date" or "datetime-local" or "month" or "week" => RaskString.ValidationDate,
        "time" => RaskString.ValidationTime,
        _ => null,
    };

    /// <summary>The sentence for text that arrived and the member's type cannot hold, or null when it has none.</summary>
    internal static RaskString? Unparsed(Type member, string raw)
    {
        var sentence = OfType(member);
        if (!IsNumeric(member) || NumberText.Invariant(raw) is not { } text || !IsNumber(text, out var whole))
        {
            return sentence;
        }

        // A number the type would take the shape of, and still cannot hold: it lies outside what the type holds.
        return whole || sentence == RaskString.ValidationNumber ? RaskString.ValidationNumberRange : sentence;
    }

    /// <summary>Whether <paramref name="member" /> holds a number, whole or not.</summary>
    internal static bool IsNumeric(Type member) =>
        OfType(member) is RaskString.ValidationWholeNumber or RaskString.ValidationNumber;

    /// <summary>Whether a parsed value is one a field can hold: not an infinity, and not "not a number".</summary>
    internal static bool IsFinite(object? parsed) => parsed switch
    {
        double number => double.IsFinite(number),
        float number => float.IsFinite(number),
        Half number => Half.IsFinite(number),
        _ => true,
    };

    /// <summary>The sentence in the reader's language.</summary>
    internal static string Say(RaskString sentence) => RaskStrings.Get(sentence, sentence switch
    {
        RaskString.ValidationWholeNumber => "Enter a whole number.",
        RaskString.ValidationNumber => "Enter a number.",
        RaskString.ValidationNumberRange => "The number is out of range.",
        RaskString.ValidationDate => "Enter a valid date.",
        _ => "Enter a valid time.",
    });

    private static RaskString? OfType(Type member)
    {
        var type = Nullable.GetUnderlyingType(member) ?? member;
        if (type.IsEnum)
        {
            return null;
        }

        return Type.GetTypeCode(type) switch
        {
            TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32
                or TypeCode.Int64 or TypeCode.UInt64 => RaskString.ValidationWholeNumber,
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal => RaskString.ValidationNumber,
            TypeCode.DateTime => RaskString.ValidationDate,
            _ when type == typeof(nint) || type == typeof(nuint) => RaskString.ValidationWholeNumber,
            _ when type == typeof(Half) => RaskString.ValidationNumber,
            _ when type == typeof(DateOnly) || type == typeof(DateTimeOffset) => RaskString.ValidationDate,
            _ when type == typeof(TimeOnly) || type == typeof(TimeSpan) => RaskString.ValidationTime,
            _ => null,
        };
    }

    // A number too long for a decimal is still a number, with no fraction left to speak of.
    private static bool IsNumber(string raw, out bool whole)
    {
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact))
        {
            whole = exact == decimal.Truncate(exact);
            return true;
        }

        whole = true;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }
}
