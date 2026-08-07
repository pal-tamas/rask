using System.Text;

namespace Rask.Testing;

/// <summary>
///     A deliberately small CSS-selector subset, matched against a parsed <see cref="HtmlNode" /> tree.
/// </summary>
/// <remarks>
///     <para><b>Supported:</b></para>
///     <list type="bullet">
///         <item><description><c>tag</c>, <c>*</c></description></item>
///         <item><description><c>#id</c></description></item>
///         <item><description><c>.class</c> (whole-token match, repeatable: <c>.btn.btn-primary</c>)</description></item>
///         <item><description><c>[attr]</c>, <c>[attr="value"]</c>, <c>[attr^="v"]</c>, <c>[attr$="v"]</c>, <c>[attr*="v"]</c></description></item>
///         <item><description>descendant (<c>ul li</c>) and child (<c>ul &gt; li</c>) combinators</description></item>
///         <item><description><c>:has-text("…")</c> — the element's text content contains this, case-insensitively</description></item>
///     </list>
///     <para>
///         <b>Anything else throws</b>, with the offending selector in the message. That is the whole point
///         of writing a subset rather than a partial implementation: a selector that silently matched
///         nothing because <c>:nth-child</c> was quietly ignored would turn a green test into a lie. If
///         you need more than this, the element is reachable by id or data-attribute — which is also what
///         makes the test readable.
///     </para>
/// </remarks>
internal static class HtmlSelector
{
    public static IReadOnlyList<HtmlNode> Select(HtmlNode root, string selector)
    {
        var steps = Parse(selector);
        IEnumerable<HtmlNode> current = [root];

        foreach (var step in steps)
        {
            current = step.Child
                ? current.SelectMany(n => n.Children).Where(step.Simple.Matches)
                : current.SelectMany(n => n.DescendantsAndSelf().Skip(1)).Where(step.Simple.Matches);

            // Distinct: overlapping descendant sets would otherwise report an element more than once.
            current = current.Distinct();
        }

        return current.ToList();
    }

    private static List<Step> Parse(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            throw new ArgumentException("A selector cannot be empty.", nameof(selector));
        }

        var steps = new List<Step>();
        var child = false;
        var i = 0;

        while (i < selector.Length)
        {
            while (i < selector.Length && char.IsWhiteSpace(selector[i]))
            {
                i++;
            }

            if (i >= selector.Length)
            {
                break;
            }

            if (selector[i] == '>')
            {
                if (steps.Count == 0)
                {
                    throw Unsupported(selector, "a '>' combinator with nothing before it");
                }

                child = true;
                i++;
                continue;
            }

            steps.Add(new Step(ParseSimple(selector, ref i), child));
            child = false;
        }

        if (steps.Count == 0)
        {
            throw Unsupported(selector, "no selector at all");
        }

        return steps;
    }

    private static Simple ParseSimple(string selector, ref int i)
    {
        var simple = new Simple();
        var read = false;

        while (i < selector.Length)
        {
            var c = selector[i];
            if (char.IsWhiteSpace(c) || c == '>')
            {
                break;
            }

            switch (c)
            {
                case '*':
                    i++;
                    read = true;
                    break;

                case '#':
                    i++;
                    simple.Id = ReadIdentifier(selector, ref i);
                    read = true;
                    break;

                case '.':
                    i++;
                    simple.Classes.Add(ReadIdentifier(selector, ref i));
                    read = true;
                    break;

                case '[':
                    simple.Attributes.Add(ReadAttribute(selector, ref i));
                    read = true;
                    break;

                case ':':
                    simple.HasText = ReadHasText(selector, ref i);
                    read = true;
                    break;

                default:
                    if (!char.IsLetter(c))
                    {
                        throw Unsupported(selector, $"the character '{c}'");
                    }

                    simple.Tag = ReadIdentifier(selector, ref i).ToLowerInvariant();
                    read = true;
                    break;
            }
        }

        if (!read)
        {
            throw Unsupported(selector, "an empty step");
        }

        return simple;
    }

    private static string ReadIdentifier(string selector, ref int i)
    {
        var start = i;
        while (i < selector.Length && (char.IsLetterOrDigit(selector[i]) || selector[i] is '-' or '_'))
        {
            i++;
        }

        if (i == start)
        {
            throw Unsupported(selector, "an empty name");
        }

        return selector[start..i];
    }

    private static AttributeMatch ReadAttribute(string selector, ref int i)
    {
        i++; // '['
        var name = ReadIdentifier(selector, ref i);

        if (i < selector.Length && selector[i] == ']')
        {
            i++;
            return new AttributeMatch(name, null, AttributeOperator.Present);
        }

        var op = i < selector.Length
            ? selector[i] switch
            {
                '=' => AttributeOperator.Equals,
                '^' => AttributeOperator.StartsWith,
                '$' => AttributeOperator.EndsWith,
                '*' => AttributeOperator.Contains,
                _ => throw Unsupported(selector, $"the attribute operator '{selector[i]}'"),
            }
            : throw Unsupported(selector, "an unterminated '['");

        i += op == AttributeOperator.Equals ? 1 : 2;
        var value = ReadQuoted(selector, ref i);

        if (i >= selector.Length || selector[i] != ']')
        {
            throw Unsupported(selector, "an unterminated '['");
        }

        i++;
        return new AttributeMatch(name, value, op);
    }

    private static string ReadHasText(string selector, ref int i)
    {
        const string token = ":has-text(";
        if (!selector.AsSpan(i).StartsWith(token, StringComparison.Ordinal))
        {
            var end = selector.IndexOfAny([' ', '>', '['], i);
            var pseudo = end < 0 ? selector[i..] : selector[i..end];
            throw Unsupported(selector, $"the pseudo-class '{pseudo}'");
        }

        i += token.Length;
        var text = ReadQuoted(selector, ref i);
        if (i >= selector.Length || selector[i] != ')')
        {
            throw Unsupported(selector, "an unterminated ':has-text('");
        }

        i++;
        return text;
    }

    private static string ReadQuoted(string selector, ref int i)
    {
        if (i >= selector.Length)
        {
            throw Unsupported(selector, "a value that ends the selector");
        }

        var quote = selector[i];
        if (quote is not ('"' or '\''))
        {
            // Unquoted values are legal CSS but ambiguous to read; require quotes so the selector says
            // plainly where the value ends.
            throw Unsupported(selector, "an unquoted value — write [attr=\"value\"]");
        }

        i++;
        var sb = new StringBuilder();
        while (i < selector.Length && selector[i] != quote)
        {
            if (selector[i] == '\\' && i + 1 < selector.Length)
            {
                i++;
            }

            sb.Append(selector[i]);
            i++;
        }

        if (i >= selector.Length)
        {
            throw Unsupported(selector, "an unterminated quote");
        }

        i++;
        return sb.ToString();
    }

    private static ArgumentException Unsupported(string selector, string what) =>
        new($"Selector '{selector}' contains {what}, which Rask.Testing's selector subset does not "
            + "support. Supported: tag, *, #id, .class, [attr], [attr=\"v\"], [attr^=\"v\"], "
            + "[attr$=\"v\"], [attr*=\"v\"], ':has-text(\"…\")', and the descendant and '>' combinators. "
            + "For anything else, give the element an id or a data-* attribute and select on that — the "
            + "test reads better for it too.");

    private readonly record struct Step(Simple Simple, bool Child);

    private enum AttributeOperator
    {
        Present,
        Equals,
        StartsWith,
        EndsWith,
        Contains,
    }

    private readonly record struct AttributeMatch(string Name, string? Value, AttributeOperator Operator)
    {
        public bool Matches(HtmlNode node)
        {
            if (node.Attribute(Name) is not { } actual)
            {
                return false;
            }

            return Operator switch
            {
                AttributeOperator.Present => true,
                AttributeOperator.Equals => string.Equals(actual, Value, StringComparison.Ordinal),
                AttributeOperator.StartsWith => actual.StartsWith(Value!, StringComparison.Ordinal),
                AttributeOperator.EndsWith => actual.EndsWith(Value!, StringComparison.Ordinal),
                AttributeOperator.Contains => actual.Contains(Value!, StringComparison.Ordinal),
                _ => false,
            };
        }
    }

    private sealed class Simple
    {
        public string? Tag { get; set; }
        public string? Id { get; set; }
        public List<string> Classes { get; } = [];
        public List<AttributeMatch> Attributes { get; } = [];
        public string? HasText { get; set; }

        public bool Matches(HtmlNode node)
        {
            if (Tag is not null && !string.Equals(node.Tag, Tag, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (Id is not null && !string.Equals(node.Id, Id, StringComparison.Ordinal))
            {
                return false;
            }

            foreach (var cls in Classes)
            {
                if (!node.HasClass(cls))
                {
                    return false;
                }
            }

            foreach (var attribute in Attributes)
            {
                if (!attribute.Matches(node))
                {
                    return false;
                }
            }

            return HasText is null
                   || node.TextContent.Contains(HasText, StringComparison.OrdinalIgnoreCase);
        }
    }
}
