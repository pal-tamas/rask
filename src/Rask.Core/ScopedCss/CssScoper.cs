using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rask.Core.ScopedCss;

internal static class CssScoper
{
    private static readonly ConcurrentDictionary<Type, string> _scopeIds = new();

    public static string ScopeIdFor(Type type)
    {
        return _scopeIds.GetOrAdd(type, static t =>
        {
            var fqn = t.FullName ?? t.Name;
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(Encoding.UTF8.GetBytes(fqn), hash);
            var sb = new StringBuilder(2 + 8);
            sb.Append("r-");
            for (var i = 0; i < 4; i++)
            {
                sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        });
    }

    public static string Rewrite(string css, string scopeId)
    {
        if (string.IsNullOrWhiteSpace(css))
        {
            return string.Empty;
        }

        var stripped = StripComments(css);
        var suffix = $"[data-{scopeId}]";
        var sb = new StringBuilder(stripped.Length + 32);
        RewriteBlock(stripped, 0, stripped.Length, suffix, sb);
        return sb.ToString();
    }

    private static void RewriteBlock(string css, int start, int end, string suffix, StringBuilder sb)
    {
        var i = start;
        while (i < end)
        {
            if (char.IsWhiteSpace(css[i]))
            {
                sb.Append(css[i++]);
                continue;
            }

            var preludeStart = i;
            i = ScanPrelude(css, i, end);

            if (i >= end)
            {
                sb.Append(css, preludeStart, end - preludeStart);
                break;
            }

            if (css[i] == '}')
            {
                sb.Append(css, preludeStart, i - preludeStart);
                break;
            }

            var prelude = css.Substring(preludeStart, i - preludeStart);

            if (css[i] == ';')
            {
                sb.Append(prelude).Append(';');
                i++;
                continue;
            }

            var bodyStart = i + 1;
            i = FindBlockClose(css, bodyStart, end);
            AppendRule(css, prelude, bodyStart, i - bodyStart, suffix, sb);

            if (i < end)
            {
                i++;
            }
        }
    }

    // The index of the first top-level `{`, `;` or `}` from i on — or end, when the prelude runs out.
    private static int ScanPrelude(string css, int i, int end)
    {
        var parenDepth = 0;
        while (i < end)
        {
            var c = css[i];
            if (c == '(')
            {
                parenDepth++;
            }
            else if (c == ')')
            {
                parenDepth = Math.Max(0, parenDepth - 1);
            }
            else if (parenDepth == 0 && (c == '{' || c == ';' || c == '}'))
            {
                break;
            }

            i++;
        }

        return i;
    }

    // The index of the `}` closing the block whose body starts at bodyStart — or end, when it is unclosed.
    private static int FindBlockClose(string css, int bodyStart, int end)
    {
        var i = bodyStart;
        var depth = 1;
        while (i < end && depth > 0)
        {
            if (css[i] == '{')
            {
                depth++;
            }
            else if (css[i] == '}')
            {
                depth--;
            }

            if (depth > 0)
            {
                i++;
            }
        }

        return i;
    }

    private static void AppendRule(string css, string prelude, int bodyStart, int bodyLength, string suffix, StringBuilder sb)
    {
        var trimmedPrelude = prelude.TrimStart();
        if (StartsWithAtRule(trimmedPrelude, "@media") ||
            StartsWithAtRule(trimmedPrelude, "@supports") ||
            StartsWithAtRule(trimmedPrelude, "@container") ||
            StartsWithAtRule(trimmedPrelude, "@layer"))
        {
            sb.Append(prelude).Append('{');
            RewriteBlock(css, bodyStart, bodyStart + bodyLength, suffix, sb);
            sb.Append('}');
        }
        else if (trimmedPrelude.StartsWith('@'))
        {
            sb.Append(prelude).Append('{').Append(css, bodyStart, bodyLength).Append('}');
        }
        else
        {
            AppendRewrittenSelectorList(prelude, suffix, sb);
            sb.Append('{').Append(css, bodyStart, bodyLength).Append('}');
        }
    }

    private static void AppendRewrittenSelectorList(string prelude, string suffix, StringBuilder sb)
    {
        var first = true;
        foreach (var range in SplitTopLevelCommas(prelude))
        {
            if (!first)
            {
                sb.Append(',');
            }

            first = false;
            AppendScopedSelector(prelude, range.Start, range.End, suffix, sb);
        }
    }

    private static IEnumerable<(int Start, int End)> SplitTopLevelCommas(string s)
    {
        int parenDepth = 0, bracketDepth = 0, start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '(')
            {
                parenDepth++;
            }
            else if (c == ')')
            {
                parenDepth = Math.Max(0, parenDepth - 1);
            }
            else if (c == '[')
            {
                bracketDepth++;
            }
            else if (c == ']')
            {
                bracketDepth = Math.Max(0, bracketDepth - 1);
            }
            else if (c == ',' && parenDepth == 0 && bracketDepth == 0)
            {
                yield return (start, i);
                start = i + 1;
            }
        }

        yield return (start, s.Length);
    }

    private static void AppendScopedSelector(string source, int start, int end, string suffix, StringBuilder sb)
    {
        var s = start;
        while (s < end && char.IsWhiteSpace(source[s]))
        {
            sb.Append(source[s]);
            s++;
        }

        var e = end;
        while (e > s && char.IsWhiteSpace(source[e - 1]))
        {
            e--;
        }

        if (e <= s)
        {
            return;
        }

        var insertAt = FindPseudoStart(source, FindLastCompoundStart(source, s, e), e);

        sb.Append(source, s, insertAt - s);
        sb.Append(suffix);
        sb.Append(source, insertAt, e - insertAt);
        if (e < end)
        {
            sb.Append(source, e, end - e);
        }
    }

    // Where the selector's last compound starts: just past its last top-level combinator.
    private static int FindLastCompoundStart(string source, int s, int e)
    {
        var lastCompoundStart = s;
        int parenDepth = 0, bracketDepth = 0;
        var i = s;
        while (i < e)
        {
            var c = source[i];
            if (c == '(')
            {
                parenDepth++;
            }
            else if (c == ')')
            {
                parenDepth = Math.Max(0, parenDepth - 1);
            }
            else if (c == '[')
            {
                bracketDepth++;
            }
            else if (c == ']')
            {
                bracketDepth = Math.Max(0, bracketDepth - 1);
            }
            else if (parenDepth == 0 && bracketDepth == 0 && IsCombinator(c))
            {
                while (i < e && IsCombinator(source[i]))
                {
                    i++;
                }

                lastCompoundStart = i;
                continue;
            }

            i++;
        }

        return lastCompoundStart;
    }

    // Where the scope attribute goes in the compound starting at `from`: before its first top-level
    // pseudo-class or pseudo-element, or at the end when it has none.
    private static int FindPseudoStart(string source, int from, int e)
    {
        int parenDepth = 0, bracketDepth = 0;
        for (var i = from; i < e; i++)
        {
            var c = source[i];
            if (c == '(')
            {
                parenDepth++;
            }
            else if (c == ')')
            {
                parenDepth = Math.Max(0, parenDepth - 1);
            }
            else if (c == '[')
            {
                bracketDepth++;
            }
            else if (c == ']')
            {
                bracketDepth = Math.Max(0, bracketDepth - 1);
            }
            else if (c == ':' && parenDepth == 0 && bracketDepth == 0)
            {
                return i;
            }
        }

        return e;
    }

    private static bool IsCombinator(char c) =>
        c is ' ' or '\t' or '\n' or '\r' or '>' or '+' or '~';

    private static bool StartsWithAtRule(string s, string atRule)
    {
        if (!s.StartsWith(atRule, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (s.Length == atRule.Length)
        {
            return true;
        }

        var next = s[atRule.Length];
        return next == ' ' || next == '\t' || next == '\n' || next == '\r' || next == '(' || next == '{';
    }

    private static string StripComments(string css)
    {
        if (css.IndexOf("/*", StringComparison.Ordinal) < 0)
        {
            return css;
        }

        var sb = new StringBuilder(css.Length);
        var i = 0;
        while (i < css.Length)
        {
            if (i + 1 < css.Length && css[i] == '/' && css[i + 1] == '*')
            {
                var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    break;
                }

                i = end + 2;
            }
            else
            {
                sb.Append(css[i++]);
            }
        }

        return sb.ToString();
    }
}
