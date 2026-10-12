using System.Globalization;
using System.Text;

namespace Rask.UiTests.Cascade;

/// <summary>What a selector says about one element looked at on its own.</summary>
internal enum Holds
{
    /// <summary>It cannot match this element.</summary>
    No,

    /// <summary>It matches only in a state or a place the element is not in as rendered: hovered, dark, inside something.</summary>
    Sometimes,

    /// <summary>It matches where the element sits among its siblings, which a render of it alone does not say.</summary>
    ByPosition,

    /// <summary>It matches the element as rendered.</summary>
    Yes,
}

/// <summary>An element as a stylesheet sees it: its tag, its classes and its attributes.</summary>
internal sealed record StyledElement(string Tag, IReadOnlySet<string> Classes, IReadOnlyDictionary<string, string> Attributes);

/// <summary>
///     One complex selector of a compiled sheet: what it weighs, and whether it matches an element at rest.
/// </summary>
/// <remarks>
///     Enough of CSS for what Tailwind and the kit's own rules emit — compounds, the four combinators, attribute
///     tests, <c>:where()</c> / <c>:is()</c> / <c>:not()</c> — and deliberately cautious past that: any other
///     pseudo-class is a state, and a selector that looks at an ancestor or a sibling is a place.
/// </remarks>
internal sealed class CssSelector
{
    private static readonly HashSet<string> Positions = new(StringComparer.Ordinal)
    {
        "first-child", "last-child", "only-child", "first-of-type", "last-of-type", "only-of-type",
        "nth-child", "nth-last-child", "nth-of-type", "nth-last-of-type",
    };

    private readonly List<Simple> _subject;
    private readonly bool _placed;

    private CssSelector(List<Simple> subject, bool placed, (int Ids, int Classes, int Tags) weight)
    {
        _subject = subject;
        _placed = placed;
        Weight = weight;
    }

    /// <summary>Its specificity: ids, then classes with attributes and pseudo-classes, then tags.</summary>
    public (int Ids, int Classes, int Tags) Weight { get; }

    /// <summary>The classes its subject must carry, wherever in the compound they are asked for.</summary>
    public IEnumerable<string> SubjectClasses() => _subject.SelectMany(simple => simple.Classes());

    /// <summary>Every complex selector of a comma-separated list.</summary>
    public static List<CssSelector> ParseList(string text) => [.. SplitTopLevel(text, ',').Select(Parse)];

    public Holds On(StyledElement element)
    {
        var holds = Holds.Yes;
        foreach (var simple in _subject)
        {
            holds = Least(holds, simple.On(element));
        }

        return holds is not Holds.No && _placed ? Holds.Sometimes : holds;
    }

    private static Holds Least(Holds a, Holds b) => (Holds)Math.Min((int)a, (int)b);

    private static CssSelector Parse(string text)
    {
        var compounds = new List<List<Simple>> { new() };
        var weight = (Ids: 0, Classes: 0, Tags: 0);
        var at = 0;

        while (at < text.Length)
        {
            var c = text[at];
            if (char.IsWhiteSpace(c) || c is '>' or '+' or '~')
            {
                at++;
                if (compounds[^1].Count > 0)
                {
                    compounds.Add([]);
                }

                continue;
            }

            var simple = ReadSimple(text, ref at);
            weight = (weight.Ids + simple.Weight.Ids, weight.Classes + simple.Weight.Classes, weight.Tags + simple.Weight.Tags);
            compounds[^1].Add(simple);
        }

        compounds.RemoveAll(compound => compound.Count == 0);
        return new CssSelector(compounds.Count == 0 ? [] : compounds[^1], compounds.Count > 1, weight);
    }

    private static Simple ReadSimple(string text, ref int at)
    {
        switch (text[at])
        {
            case '*':
            case '&':
                at++;
                return new Simple(Kind.Any, "", null, []);
            case '.':
                at++;
                return new Simple(Kind.Class, ReadName(text, ref at), null, []);
            case '#':
                at++;
                return new Simple(Kind.Id, ReadName(text, ref at), null, []);
            case '[':
                return ReadAttribute(Balanced(text, ref at, '[', ']'));
            case ':':
                return ReadPseudo(text, ref at);
            default:
                return new Simple(Kind.Tag, ReadName(text, ref at), null, []);
        }
    }

    private static Simple ReadPseudo(string text, ref int at)
    {
        var element = at + 1 < text.Length && text[at + 1] == ':';
        at += element ? 2 : 1;
        var name = ReadName(text, ref at);
        var argument = at < text.Length && text[at] == '(' ? Balanced(text, ref at, '(', ')') : null;

        if (element)
        {
            return new Simple(Kind.PseudoElement, name, argument, []);
        }

        return name is "where" or "is" or "not" or "has"
            ? new Simple(Kind.Pseudo, name, argument, ParseList(argument ?? ""))
            : new Simple(Kind.Pseudo, name, argument, []);
    }

    private static Simple ReadAttribute(string inside)
    {
        var split = inside.IndexOf('=', StringComparison.Ordinal);
        if (split < 0)
        {
            return new Simple(Kind.Attribute, Unescape(inside.Trim()), null, []);
        }

        var prefixed = split > 0 && inside[split - 1] is '~' or '|' or '^' or '$' or '*';
        var name = inside[..(prefixed ? split - 1 : split)].Trim();
        var value = inside[(split + 1)..].Trim();
        if (value.EndsWith(" i", StringComparison.Ordinal) || value.EndsWith(" s", StringComparison.Ordinal))
        {
            value = value[..^2].Trim();
        }

        return new Simple(Kind.Attribute, Unescape(name), (prefixed ? inside[split - 1] : '=') + Unescape(value.Trim('"', '\'')), []);
    }

    // The text between a pair of brackets that opens at `at`, which is left after the closing one.
    private static string Balanced(string text, ref int at, char open, char close)
    {
        var start = ++at;
        var depth = 1;
        char quote = default;
        for (; at < text.Length; at++)
        {
            var c = text[at];
            if (c == '\\')
            {
                at++;
            }
            else if (quote != default)
            {
                quote = c == quote ? default : quote;
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == open)
            {
                depth++;
            }
            else if (c == close && --depth == 0)
            {
                break;
            }
        }

        return text[start..Math.Min(at++, text.Length)];
    }

    private static string ReadName(string text, ref int at)
    {
        var start = at;
        while (at < text.Length)
        {
            var c = text[at];
            if (c == '\\')
            {
                at = EscapeEnd(text, at);
            }
            else if (char.IsLetterOrDigit(c) || c is '-' or '_' || c > 127)
            {
                at++;
            }
            else
            {
                break;
            }
        }

        return Unescape(text[start..Math.Min(at, text.Length)]);
    }

    // Where the escape that starts at `at` ends: `\:` is one character, `\32 ` a code point and the space that closes it.
    private static int EscapeEnd(string text, int at)
    {
        var hex = at + 1;
        while (hex < text.Length && hex - at <= 6 && Uri.IsHexDigit(text[hex]))
        {
            hex++;
        }

        if (hex == at + 1)
        {
            return Math.Min(at + 2, text.Length);
        }

        return hex < text.Length && text[hex] == ' ' ? hex + 1 : hex;
    }

    private static string Unescape(string name)
    {
        if (!name.Contains('\\', StringComparison.Ordinal))
        {
            return name;
        }

        var sb = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] != '\\' || i + 1 == name.Length)
            {
                sb.Append(name[i]);
                continue;
            }

            var end = EscapeEnd(name, i);
            var digits = name[(i + 1)..end].TrimEnd(' ');
            sb.Append(digits.Length > 0 && digits.All(Uri.IsHexDigit)
                ? char.ConvertFromUtf32(int.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
                : name[i + 1].ToString());
            i = end - 1;
        }

        return sb.ToString();
    }

    /// <summary>Splits at a separator that is outside every bracket, string and escape.</summary>
    internal static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        char quote = default;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\')
            {
                i++;
            }
            else if (quote != default)
            {
                quote = c == quote ? default : quote;
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                depth--;
            }
            else if (c == separator && depth == 0)
            {
                parts.Add(text[start..i].Trim());
                start = i + 1;
            }
        }

        parts.Add(text[start..].Trim());
        parts.RemoveAll(part => part.Length == 0);
        return parts;
    }

    private enum Kind
    {
        Any,
        Tag,
        Class,
        Id,
        Attribute,
        Pseudo,
        PseudoElement,
    }

    private sealed record Simple(Kind Kind, string Name, string? Argument, List<CssSelector> Inner)
    {
        public (int Ids, int Classes, int Tags) Weight => Kind switch
        {
            Kind.Any => (0, 0, 0),
            Kind.Id => (1, 0, 0),
            Kind.Tag or Kind.PseudoElement => (0, 0, 1),
            Kind.Pseudo when Name is "where" => (0, 0, 0),
            Kind.Pseudo when Inner.Count > 0 => Inner.Max(inner => inner.Weight),
            _ => (0, 1, 0),
        };

        public IEnumerable<string> Classes() => Kind switch
        {
            Kind.Class => [Name],
            Kind.Pseudo when Name is "where" or "is" && Inner.Count == 1 => Inner[0].SubjectClasses(),
            _ => [],
        };

        public Holds On(StyledElement element) => Kind switch
        {
            Kind.Any => Holds.Yes,
            Kind.Tag => string.Equals(Name, element.Tag, StringComparison.OrdinalIgnoreCase) ? Holds.Yes : Holds.No,
            Kind.Class => element.Classes.Contains(Name) ? Holds.Yes : Holds.No,
            Kind.Id => element.Attributes.TryGetValue("id", out var id) && id == Name ? Holds.Yes : Holds.No,
            Kind.Attribute => Attribute(element) ? Holds.Yes : Holds.No,
            // Another box than the element's own.
            Kind.PseudoElement => Holds.No,
            _ => Pseudo(element),
        };

        private bool Attribute(StyledElement element)
        {
            if (!element.Attributes.TryGetValue(Name, out var actual))
            {
                return false;
            }

            if (Argument is null)
            {
                return true;
            }

            var wanted = Argument[1..];
            return Argument[0] switch
            {
                '~' => actual.Split(' ').Contains(wanted, StringComparer.Ordinal),
                '|' => actual == wanted || actual.StartsWith(wanted + "-", StringComparison.Ordinal),
                '^' => actual.StartsWith(wanted, StringComparison.Ordinal),
                '$' => actual.EndsWith(wanted, StringComparison.Ordinal),
                '*' => actual.Contains(wanted, StringComparison.Ordinal),
                _ => actual == wanted,
            };
        }

        private Holds Pseudo(StyledElement element)
        {
            if (Positions.Contains(Name))
            {
                return Holds.ByPosition;
            }

            var inner = Inner.Count == 0 ? Holds.Sometimes : Inner.Max(selector => selector.On(element));
            return Name switch
            {
                "where" or "is" => inner,
                // What is true as rendered is false negated; a state or a position may go either way.
                "not" => inner switch
                {
                    Holds.No => Holds.Yes,
                    Holds.Yes => Holds.No,
                    _ => Holds.ByPosition,
                },
                "dir" => Argument == "ltr" ? Holds.Yes : Holds.No,
                "root" or "host" => Holds.No,
                _ => Holds.Sometimes,
            };
        }
    }
}
