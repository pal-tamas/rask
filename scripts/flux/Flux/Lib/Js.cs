using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rask.Flux;

/// <summary>
///     What the measurements need of JavaScript's own arithmetic and printing, so a report and a file this harness
///     writes read exactly as the browser-side numbers do: <c>Math.round</c>, <c>String(number)</c> and
///     <c>JSON.stringify</c>.
/// </summary>
public static class Js
{
    private static readonly JsonSerializerOptions Plain = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary><c>Math.round</c>: half-way rounds up, towards positive infinity.</summary>
    public static double Round(double value) => Math.Floor(value + 0.5);

    /// <summary><c>Math.round(value * 100) / 100</c>.</summary>
    public static double Round2(double value) => Round(value * 100) / 100;

    /// <summary><c>String(number)</c>: the shortest digits that read back as the number, laid out as ECMAScript does.</summary>
    public static string Num(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "Infinity" : "-Infinity";
        }

        if (value == 0)
        {
            return "0";
        }

        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var negative = text[0] == '-';
        if (negative)
        {
            text = text[1..];
        }

        var exponent = 0;
        var e = text.IndexOfAny(['E', 'e']);
        if (e >= 0)
        {
            exponent = int.Parse(text[(e + 1)..], CultureInfo.InvariantCulture);
            text = text[..e];
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var digits = dot >= 0 ? text[..dot] + text[(dot + 1)..] : text;
        var point = dot >= 0 ? dot : text.Length;
        var lead = 0;
        while (lead < digits.Length - 1 && digits[lead] == '0')
        {
            lead++;
        }

        digits = digits[lead..].TrimEnd('0');
        point -= lead;
        if (digits.Length == 0)
        {
            return "0";
        }

        // value = 0.digits × 10^n, as the specification's Number::toString states it.
        var n = point + exponent;
        var k = digits.Length;
        string printed;
        if (k <= n && n <= 21)
        {
            printed = digits + new string('0', n - k);
        }
        else if (n > 0 && n <= 21)
        {
            printed = digits[..n] + "." + digits[n..];
        }
        else if (n > -6 && n <= 0)
        {
            printed = "0." + new string('0', -n) + digits;
        }
        else
        {
            var power = n - 1;
            var sign = power < 0 ? "-" : "+";
            printed = (k == 1 ? digits : digits[..1] + "." + digits[1..]) + "e" + sign + Math.Abs(power).ToString(CultureInfo.InvariantCulture);
        }

        return negative ? "-" + printed : printed;
    }

    /// <summary>A number parsed as <c>Number(text)</c> does for the decimals the comparisons round.</summary>
    public static double Parse(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary><c>JSON.stringify(node)</c>, or with <paramref name="indent" /> spaces a level as <c>JSON.stringify(node, null, indent)</c>.</summary>
    public static string Stringify(JsonNode? node, int indent = 0)
    {
        var builder = new StringBuilder();
        Write(builder, node, indent, 0);
        return builder.ToString();
    }

    /// <summary>Any value as JSON, through System.Text.Json, for an argument handed to the page.</summary>
    public static string Serialize(object? value) => value is JsonNode node ? node.ToJsonString(Plain) : JsonSerializer.Serialize(value, Plain);

    /// <summary>A parsed copy of what <paramref name="value" /> serializes to.</summary>
    public static JsonNode? ToNode(object? value) => JsonNode.Parse(Serialize(value));

    /// <summary>The number in <paramref name="node" />, whatever .NET type holds it.</summary>
    public static double Number(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return double.NaN;
        }

        if (value.TryGetValue<double>(out var d))
        {
            return d;
        }

        if (value.TryGetValue<int>(out var i))
        {
            return i;
        }

        if (value.TryGetValue<long>(out var l))
        {
            return l;
        }

        if (value.TryGetValue<JsonElement>(out var element) && element.ValueKind == JsonValueKind.Number)
        {
            return element.GetDouble();
        }

        return value.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN;
    }

    /// <summary>The string in <paramref name="node" />, or null where it is no string.</summary>
    public static string? Str(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    /// <summary>JavaScript's truthiness of a JSON value.</summary>
    public static bool Truthy(JsonNode? node) => node switch
    {
        null => false,
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.False or JsonValueKind.Null or JsonValueKind.Undefined => false,
            JsonValueKind.String => value.GetValue<string>().Length > 0,
            JsonValueKind.Number => Number(value) is var n && n != 0 && !double.IsNaN(n),
            _ => true,
        },
        _ => true,
    };

    private static void Write(StringBuilder builder, JsonNode? node, int indent, int depth)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                break;
            case JsonObject obj:
                if (obj.Count == 0)
                {
                    builder.Append("{}");
                    break;
                }

                builder.Append('{');
                var first = true;
                foreach (var (key, value) in obj)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    Break(builder, indent, depth + 1);
                    Quote(builder, key);
                    builder.Append(indent > 0 ? ": " : ":");
                    Write(builder, value, indent, depth + 1);
                }

                Break(builder, indent, depth);
                builder.Append('}');
                break;
            case JsonArray array:
                if (array.Count == 0)
                {
                    builder.Append("[]");
                    break;
                }

                builder.Append('[');
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    Break(builder, indent, depth + 1);
                    Write(builder, array[i], indent, depth + 1);
                }

                Break(builder, indent, depth);
                builder.Append(']');
                break;
            case JsonValue value:
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String:
                        Quote(builder, value.GetValue<string>());
                        break;
                    case JsonValueKind.Number:
                        var n = Number(value);
                        builder.Append(double.IsFinite(n) ? Num(n) : "null");
                        break;
                    case JsonValueKind.True:
                        builder.Append("true");
                        break;
                    case JsonValueKind.False:
                        builder.Append("false");
                        break;
                    default:
                        builder.Append("null");
                        break;
                }

                break;
        }
    }

    private static void Break(StringBuilder builder, int indent, int depth)
    {
        if (indent > 0)
        {
            builder.Append('\n').Append(' ', indent * depth);
        }
    }

    /// <summary>A string as JSON.stringify quotes it: only the quote, the backslash and the control characters escaped.</summary>
    public static void Quote(StringBuilder builder, string text)
    {
        builder.Append('"');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    var lone = char.IsSurrogate(c) && !(char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                        && !(char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(text[i - 1]));
                    if (c < 0x20 || lone)
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    /// <summary>A string quoted as <c>JSON.stringify</c> quotes it.</summary>
    public static string Quote(string text)
    {
        var builder = new StringBuilder();
        Quote(builder, text);
        return builder.ToString();
    }
}
