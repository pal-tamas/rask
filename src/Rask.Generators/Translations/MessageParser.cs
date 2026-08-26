using System;
using System.Collections.Generic;
using System.Text;

namespace Rask.Generators.Translations;

// One placeholder in a message: the name a caller sees, the CLR type its parameter takes, and any
// .NET format specifier to apply.
internal sealed class Placeholder(string name, string clrType, string? format)
{
    public string Name { get; } = name;
    public string ClrType { get; } = clrType;
    public string? Format { get; } = format;
}

// A message with its placeholders lifted out: "Hello, {name}!" becomes "Hello, {0}!" plus one
// placeholder called name.
internal sealed class ParsedMessage(string format, List<Placeholder> placeholders, string? error)
{
    public string Format { get; } = format;
    public List<Placeholder> Placeholders { get; } = placeholders;
    public string? Error { get; } = error;
}

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

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c == '{')
            {
                if (i + 1 < value.Length && value[i + 1] == '{')
                {
                    format.Append("{{");
                    i++;
                    continue;
                }

                var close = value.IndexOf('}', i + 1);
                if (close < 0)
                {
                    return Fail("an unclosed '{' — write '{{' for a literal brace");
                }

                var body = value.Substring(i + 1, close - i - 1);
                i = close;

                if (body.Length == 0)
                {
                    return Fail("an empty placeholder '{}'");
                }

                var parts = body.Split(':');
                var name = parts[0].Trim();
                if (name.Length == 0)
                {
                    return Fail("a placeholder with no name");
                }

                var positional = IsAllDigits(name);
                if (positional)
                {
                    sawPositional = true;
                    name = "arg" + name;
                }
                else
                {
                    sawNamed = true;
                    if (!IsIdentifier(name))
                    {
                        return Fail($"'{name}' is not usable as a parameter name");
                    }
                }

                if (sawNamed && sawPositional)
                {
                    return Fail("a mix of positional {0} and named {name} placeholders — use one or the other");
                }

                var clrType = "object?";
                string? fmt = null;
                if (parts.Length > 1)
                {
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

                    if (fmt is { Length: 0 })
                    {
                        fmt = null;
                    }
                }

                if (!byName.TryGetValue(name, out var index))
                {
                    index = placeholders.Count;
                    byName[name] = index;
                    placeholders.Add(new Placeholder(name, clrType, fmt));
                }

                format.Append('{').Append(index);
                if (fmt is not null)
                {
                    format.Append(':').Append(fmt);
                }

                format.Append('}');
                continue;
            }

            if (c == '}')
            {
                if (i + 1 < value.Length && value[i + 1] == '}')
                {
                    format.Append("}}");
                    i++;
                    continue;
                }

                return Fail("a stray '}' — write '}}' for a literal brace");
            }

            format.Append(c);
        }

        return new ParsedMessage(format.ToString(), placeholders, null);

        static ParsedMessage Fail(string reason) => new(string.Empty, [], reason);
    }

    private static bool IsAllDigits(string s)
    {
        foreach (var c in s)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return s.Length > 0;
    }

    private static bool IsIdentifier(string s)
    {
        if (s.Length == 0 || (!char.IsLetter(s[0]) && s[0] != '_'))
        {
            return false;
        }

        foreach (var c in s)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
