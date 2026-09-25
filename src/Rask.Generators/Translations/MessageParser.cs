using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rask.Generators.Translations;

/// <summary>
///     Turns a catalog value into a <c>string.Format</c> template plus a typed parameter list.
/// </summary>
/// <remarks>
///     <para>
///         Placeholders are named — <c>{name}</c>, optionally <c>{count:int}</c>, optionally
///         <c>{price:decimal:C}</c>. Named rather than positional because that is what makes reordering
///         across languages <em>checkable</em>: Hungarian will move the arguments, so the correctness
///         rule is that the SET of names matches the neutral catalog, not that the order does.
///     </para>
///     <para>
///         Positional <c>{0}</c> is accepted as sugar so the obvious first thing anyone writes compiles,
///         but a message may not mix the two — the resulting parameter list would be ambiguous to read
///         and trivial to get wrong at the call site.
///     </para>
/// </remarks>
internal static class MessageParser
{
    private static readonly Dictionary<string, string> _types = new(StringComparer.Ordinal)
    {
        ["string"] = "string",
        ["int"] = "int",
        ["long"] = "long",
        ["double"] = "double",
        ["decimal"] = "decimal",
        ["float"] = "float",
        ["bool"] = "bool",
        ["DateTime"] = "global::System.DateTime",
        ["DateOnly"] = "global::System.DateOnly",
        ["TimeOnly"] = "global::System.TimeOnly",
        ["TimeSpan"] = "global::System.TimeSpan",
        ["Guid"] = "global::System.Guid",
    };

    public static ParsedMessage Parse(string value)
    {
        var format = new StringBuilder();
        var placeholders = new List<Placeholder>();
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        var sawNamed = false;
        var sawPositional = false;

        var i = 0;
        while (i < value.Length)
        {
            var c = value[i];
            var doubled = i + 1 < value.Length && value[i + 1] == c;

            if (c == '{' && !doubled)
            {
                var close = value.IndexOf('}', i + 1);
                if (close < 0)
                {
                    return Fail("an unclosed '{' — write '{{' for a literal brace");
                }

                var error = ReadPlaceholder(
                    value.Substring(i + 1, close - i - 1), ref sawNamed, ref sawPositional, out var placeholder);
                if (error is not null)
                {
                    return Fail(error);
                }

                AppendPlaceholder(format, placeholders, byName, placeholder!);
                i = close + 1;
                continue;
            }

            if (c == '}' && !doubled)
            {
                return Fail("a stray '}' — write '}}' for a literal brace");
            }

            if (c is '{' or '}')
            {
                format.Append(c).Append(c);
                i += 2;
                continue;
            }

            format.Append(c);
            i++;
        }

        return new ParsedMessage(format.ToString(), placeholders, null);

        static ParsedMessage Fail(string reason) => new(string.Empty, [], reason);
    }

    // One `{…}` body — name, then an optional type keyword or format specifier. Returns the reason it is
    // unusable, or null with the placeholder it describes.
    private static string? ReadPlaceholder(
        string body, ref bool sawNamed, ref bool sawPositional, out Placeholder? placeholder)
    {
        placeholder = null;
        if (body.Length == 0)
        {
            return "an empty placeholder '{}'";
        }

        var parts = body.Split(':');
        var name = parts[0].Trim();
        if (name.Length == 0)
        {
            return "a placeholder with no name";
        }

        if (IsAllDigits(name))
        {
            sawPositional = true;
            name = "arg" + name;
        }
        else
        {
            sawNamed = true;
            if (!IsIdentifier(name))
            {
                return $"'{name}' is not usable as a parameter name";
            }
        }

        if (sawNamed && sawPositional)
        {
            return "a mix of positional {0} and named {name} placeholders — use one or the other";
        }

        var (clrType, fmt) = TypeAndFormat(parts);
        placeholder = new Placeholder(name, clrType, fmt);
        return null;
    }

    private static (string ClrType, string? Format) TypeAndFormat(string[] parts)
    {
        if (parts.Length == 1)
        {
            return ("object?", null);
        }

        var clrType = "object?";
        string? fmt = null;
        var typeToken = parts[1].Trim();
        if (typeToken.Length > 0 && _types.TryGetValue(typeToken, out var mapped))
        {
            clrType = mapped;
            if (parts.Length > 2)
            {
                fmt = string.Join(":", parts, 2, parts.Length - 2).Trim();
            }
        }
        else
        {
            // Not a type keyword, so the rest is a .NET format specifier: {when::d} and
            // {when:d} mean the same thing.
            fmt = string.Join(":", parts, 1, parts.Length - 1).Trim();
        }

        return (clrType, fmt is { Length: 0 } ? null : fmt);
    }

    private static void AppendPlaceholder(
        StringBuilder format, List<Placeholder> placeholders, Dictionary<string, int> byName, Placeholder placeholder)
    {
        if (!byName.TryGetValue(placeholder.Name, out var index))
        {
            index = placeholders.Count;
            byName[placeholder.Name] = index;
            placeholders.Add(placeholder);
        }

        format.Append('{').Append(index);
        if (placeholder.Format is not null)
        {
            format.Append(':').Append(placeholder.Format);
        }

        format.Append('}');
    }

    private static bool IsAllDigits(string s) => s.Length > 0 && s.All(static c => c is >= '0' and <= '9');

    private static bool IsIdentifier(string s) =>
        s.Length > 0
        && (char.IsLetter(s[0]) || s[0] == '_')
        && s.All(static c => char.IsLetterOrDigit(c) || c == '_');
}
