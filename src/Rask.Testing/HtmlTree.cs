using System.Text;

namespace Rask.Testing;

/// <summary>
///     Parses the markup Rask's own serializer produced into a small <see cref="HtmlNode" /> tree.
/// </summary>
/// <remarks>
///     <para>
///         <b>Not a general-purpose HTML parser, and shouldn't be pointed at one's output.</b> It reads
///         what <c>HtmlSerializer</c> emits, and that is a narrow, known shape: attributes are always
///         double-quoted, values always encode <c>&lt;</c>, <c>&gt;</c> and <c>"</c>, void elements are
///         written self-closing, and every non-void element gets its closing tag. Those guarantees are
///         what make a ~200-line reader correct here and what let <c>Rask.Testing</c> keep its single
///         dependency instead of taking one on a full parser — a testing package's dependencies land in
///         every consumer's test project.
///     </para>
///     <para>
///         Mis-nesting is tolerated rather than diagnosed: a stray close tag that matches no open element
///         is ignored, and anything still open at the end is closed implicitly. The serializer does not
///         produce either, so treating them as errors would only turn a framework bug into a confusing
///         parser exception in somebody's test.
///     </para>
/// </remarks>
internal static class HtmlTree
{
    // Elements HTML says never have a closing tag. The serializer writes these self-closed, but a
    // hand-written fixture (or a Raw()) may not, so treat the name as authoritative either way.
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr",
    };

    // Content is CDATA-ish: '<' inside does not open a tag, so scan to the matching close tag instead.
    private static readonly HashSet<string> RawTextElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style",
    };

    public static HtmlNode Parse(string html)
    {
        var root = new HtmlNode("#root");
        var open = new Stack<HtmlNode>();
        open.Push(root);

        var i = 0;
        while (i < html.Length)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0)
            {
                AppendText(open.Peek(), html, i, html.Length);
                break;
            }

            AppendText(open.Peek(), html, i, lt);

            // <!-- comment -->, <!doctype …>, <?…> — skipped whole; none of them carry assertable content.
            if (lt + 1 < html.Length && (html[lt + 1] == '!' || html[lt + 1] == '?'))
            {
                i = SkipBogus(html, lt);
                continue;
            }

            if (lt + 1 < html.Length && html[lt + 1] == '/')
            {
                var close = html.IndexOf('>', lt);
                if (close < 0)
                {
                    break;
                }

                var name = html[(lt + 2)..close].Trim();
                CloseTo(open, name, root);
                i = close + 1;
                continue;
            }

            if (!TryReadStartTag(html, lt, out var tag, out var attributes, out var selfClosing, out var after))
            {
                // A '<' that starts no tag is text — the serializer encodes those, but a Raw() need not.
                AppendText(open.Peek(), html, lt, lt + 1);
                i = lt + 1;
                continue;
            }

            var node = new HtmlNode(tag);
            foreach (var (name, value) in attributes)
            {
                node.SetAttribute(name, value);
            }

            open.Peek().Add(node);

            if (selfClosing || VoidElements.Contains(tag))
            {
                i = after;
                continue;
            }

            if (RawTextElements.Contains(tag))
            {
                var end = html.IndexOf($"</{tag}", after, StringComparison.OrdinalIgnoreCase);
                if (end < 0)
                {
                    node.AppendText(html[after..]);
                    break;
                }

                node.AppendText(html[after..end]);
                var gt = html.IndexOf('>', end);
                i = gt < 0 ? html.Length : gt + 1;
                continue;
            }

            open.Push(node);
            i = after;
        }

        return root;
    }

    private static void AppendText(HtmlNode into, string html, int start, int end)
    {
        if (end > start)
        {
            into.AppendText(html[start..end]);
        }
    }

    // Pops to and including the nearest matching open element. A close tag matching nothing is ignored —
    // popping blindly would reparent everything after it under the wrong element, which is worse.
    private static void CloseTo(Stack<HtmlNode> open, string name, HtmlNode root)
    {
        if (!open.Any(n => string.Equals(n.Tag, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        while (open.Count > 1)
        {
            var node = open.Pop();
            if (string.Equals(node.Tag, name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        _ = root;
    }

    private static int SkipBogus(string html, int lt)
    {
        if (html.AsSpan(lt).StartsWith("<!--", StringComparison.Ordinal))
        {
            var end = html.IndexOf("-->", lt, StringComparison.Ordinal);
            return end < 0 ? html.Length : end + 3;
        }

        var gt = html.IndexOf('>', lt);
        return gt < 0 ? html.Length : gt + 1;
    }

    private static bool TryReadStartTag(
        string html,
        int lt,
        out string tag,
        out List<(string Name, string Value)> attributes,
        out bool selfClosing,
        out int after)
    {
        tag = string.Empty;
        attributes = [];
        selfClosing = false;
        after = lt + 1;

        var i = lt + 1;
        var nameStart = i;
        while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] is not ('>' or '/'))
        {
            i++;
        }

        if (i == nameStart)
        {
            return false;
        }

        tag = html[nameStart..i].ToLowerInvariant();
        if (tag.Length == 0 || !char.IsLetter(tag[0]))
        {
            return false;
        }

        while (i < html.Length)
        {
            while (i < html.Length && char.IsWhiteSpace(html[i]))
            {
                i++;
            }

            if (i >= html.Length)
            {
                break;
            }

            if (html[i] == '>')
            {
                after = i + 1;
                return true;
            }

            if (html[i] == '/')
            {
                selfClosing = true;
                i++;
                continue;
            }

            var attrStart = i;
            while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] is not ('=' or '>' or '/'))
            {
                i++;
            }

            var name = html[attrStart..i];
            if (name.Length == 0)
            {
                i++;
                continue;
            }

            while (i < html.Length && char.IsWhiteSpace(html[i]))
            {
                i++;
            }

            if (i < html.Length && html[i] == '=')
            {
                i++;
                while (i < html.Length && char.IsWhiteSpace(html[i]))
                {
                    i++;
                }

                attributes.Add((name, ReadValue(html, ref i)));
            }
            else
            {
                // A bare attribute (`defer`, `disabled`) — HTML says its value is its own name.
                attributes.Add((name, name));
            }
        }

        after = html.Length;
        return true;
    }

    private static string ReadValue(string html, ref int i)
    {
        if (i >= html.Length)
        {
            return string.Empty;
        }

        var quote = html[i];
        if (quote is '"' or '\'')
        {
            i++;
            var end = html.IndexOf(quote, i);
            if (end < 0)
            {
                var rest = html[i..];
                i = html.Length;
                return rest;
            }

            var value = html[i..end];
            i = end + 1;
            return value;
        }

        var start = i;
        while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '>')
        {
            i++;
        }

        return html[start..i];
    }

    // Only used by the debugger display; kept here so HtmlNode stays about shape rather than formatting.
    internal static string Describe(HtmlNode node)
    {
        var sb = new StringBuilder("<").Append(node.Tag);
        foreach (var (name, value) in node.Attributes)
        {
            sb.Append(' ').Append(name).Append("=\"").Append(value).Append('"');
        }

        return sb.Append('>').ToString();
    }
}
