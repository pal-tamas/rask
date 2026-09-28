using System.Text;

namespace Rask.Core.ScopedAssets;

/// <summary>
///     A conservative, dependency-free CSS minifier for the scoped-CSS bundle. It removes comments and
///     collapses insignificant whitespace — the bulk of the savings for pretty-printed, commented scoped
///     CSS — while deliberately <b>not</b> touching anything context-sensitive: whitespace is only
///     stripped immediately around the unambiguous structural delimiters <c>{</c> <c>}</c> <c>;</c>
///     <c>,</c>, so descendant combinators, <c>calc()</c> operators, and <c>:</c> in selectors keep the
///     spaces they need. String and <c>url()</c>-quote runs are copied verbatim. The transform is a pure,
///     deterministic function of its input, so the bundle stays byte-stable (its content hash / immutable
///     URL doesn't churn between builds of the same input).
/// </summary>
internal static class CssMinifier
{
    // Whitespace next to one of these is always removable — they self-delimit in CSS grammar.
    private static bool IsRemovableBoundary(char c) => c is '{' or '}' or ';' or ',';

    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\r' or '\n' or '\f';

    public static byte[] MinifyUtf8(ReadOnlySpan<byte> utf8)
    {
        var css = Encoding.UTF8.GetString(utf8);
        return Encoding.UTF8.GetBytes(Minify(css));
    }

    public static string Minify(string css)
    {
        var sb = new StringBuilder(css.Length);
        var pendingSpace = false; // an insignificant run of whitespace (or a stripped comment) is pending
        var prev = '\0';          // last emitted non-space char, for the "is a space needed here" test
        var i = 0;
        var n = css.Length;

        while (i < n)
        {
            var c = css[i];

            // Comment — drop it, but treat it as a token separator (CSS comments always separate tokens),
            // so an adjacent-token join can't happen.
            if (c == '/' && i + 1 < n && css[i + 1] == '*')
            {
                i = SkipComment(css, i + 2);
                pendingSpace = true;
                continue;
            }

            if (IsWhitespace(c))
            {
                pendingSpace = true;
                i++;
                continue;
            }

            if (pendingSpace)
            {
                AppendSpaceIfNeeded(sb, prev, c);
                pendingSpace = false;
            }

            // String literal — copy verbatim (including escapes) so its whitespace/delimiters survive.
            if (c is '"' or '\'')
            {
                i = CopyString(css, i, sb);
                prev = c;
                continue;
            }

            // A real, structural character.

            // Drop a declaration's trailing ';' right before a '}' (";}" -> "}"). Safe: string content is
            // emitted in the branch above, so a ';' sitting at the tail here is always a real terminator.
            if (c == '}' && sb.Length > 0 && sb[^1] == ';')
            {
                sb.Length--;
            }

            sb.Append(c);
            prev = c;
            i++;
        }

        return sb.ToString();
    }

    // Copies the string literal opening at `start` verbatim, escapes included; returns the index past it.
    private static int CopyString(string css, int start, StringBuilder sb)
    {
        var quote = css[start];
        sb.Append(quote);
        var i = start + 1;
        while (i < css.Length)
        {
            var d = css[i];
            sb.Append(d);
            i++;
            if (d == '\\' && i < css.Length)
            {
                sb.Append(css[i]);
                i++;
                continue;
            }

            if (d == quote)
            {
                break;
            }
        }

        return i;
    }

    // From just inside a comment, the index past its closing */ (or the end, on an unterminated comment).
    private static int SkipComment(string css, int i)
    {
        while (i + 1 < css.Length && !(css[i] == '*' && css[i + 1] == '/'))
        {
            i++;
        }

        return i + 1 < css.Length ? i + 2 : css.Length;
    }

    // Emit a single separating space only when neither side is a removable boundary.
    private static void AppendSpaceIfNeeded(StringBuilder sb, char prev, char next)
    {
        if (prev == '\0' || IsRemovableBoundary(prev) || IsRemovableBoundary(next))
        {
            return;
        }

        sb.Append(' ');
    }
}
