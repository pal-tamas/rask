using System.Collections.Concurrent;
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
                sb.Append(hash[i].ToString("x2"));
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
                else if (parenDepth == 0)
                {
                    if (c == '{' || c == ';' || c == '}')
                    {
                        break;
                    }
                }

                i++;
            }

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

            var braceOpen = i;
            var depth = 1;
            i++;
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

            var braceClose = i;
            var bodyStart = braceOpen + 1;
            var bodyLength = braceClose - bodyStart;

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
            else if (trimmedPrelude.StartsWith("@", StringComparison.Ordinal))
            {
                sb.Append(prelude).Append('{').Append(css, bodyStart, bodyLength).Append('}');
            }
            else
            {
                AppendRewrittenSelectorList(prelude, suffix, sb);
                sb.Append('{').Append(css, bodyStart, bodyLength).Append('}');
            }

            if (i < end)
            {
                i++;
            }
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

            // `:global(X)` opt-out — when the trimmed selector is wrapped in :global(),
            // emit X with no scope suffix. Used for app-global rules colocated in a
            // component's sibling .css (e.g. `:global(body) { ... }`), where the target
            // tag is a shell tag the framework never stamps data-rask-* on anyway.
            if (TryUnwrapGlobal(prelude, range.Start, range.End,
                out var leadStart, out var leadEnd,
                out var innerStart, out var innerEnd,
                out var trailStart, out var trailEnd))
            {
                sb.Append(prelude, leadStart, leadEnd - leadStart);
                sb.Append(prelude, innerStart, innerEnd - innerStart);
                sb.Append(prelude, trailStart, trailEnd - trailStart);
                continue;
            }

            AppendScopedSelector(prelude, range.Start, range.End, suffix, sb);
        }
    }

    // Returns true when the (whitespace-trimmed) selector range is exactly `:global(X)`.
    // <paramref name="leadStart"/>/<paramref name="leadEnd"/> bracket any leading
    // whitespace from the source that should be preserved so formatting around commas
    // stays intact; <paramref name="innerStart"/>/<paramref name="innerEnd"/> bracket X.
    private static bool TryUnwrapGlobal(string source, int start, int end,
        out int leadStart, out int leadEnd,
        out int innerStart, out int innerEnd,
        out int trailStart, out int trailEnd)
    {
        leadStart = start;
        leadEnd = start;
        innerStart = 0;
        innerEnd = 0;
        trailStart = end;
        trailEnd = end;

        var s = start;
        while (s < end && char.IsWhiteSpace(source[s]))
        {
            s++;
        }

        leadEnd = s;

        var e = end;
        while (e > s && char.IsWhiteSpace(source[e - 1]))
        {
            e--;
        }

        trailStart = e;
        trailEnd = end;

        const string prefix = ":global(";
        if (e - s < prefix.Length + 1)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (source[s + i] != prefix[i])
            {
                return false;
            }
        }

        if (source[e - 1] != ')')
        {
            return false;
        }

        // The closing paren must be the one that matches the opener at s. Walk parens
        // between them and confirm depth stays >0 until the very end. A nested
        // `:global(.a:not(.b))` keeps depth alive through the inner `.b` close.
        var depth = 1;
        for (var i = s + prefix.Length; i < e - 1; i++)
        {
            var c = source[i];
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return false;
                }
            }
        }

        if (depth != 1)
        {
            return false;
        }

        innerStart = s + prefix.Length;
        innerEnd = e - 1;
        return true;
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

        var lastCompoundStart = s;
        int parenDepth = 0, bracketDepth = 0;
        for (var i = s; i < e; i++)
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
            else if (parenDepth == 0 && bracketDepth == 0)
            {
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '>' || c == '+' || c == '~')
                {
                    var j = i;
                    while (j < e && (source[j] == ' ' || source[j] == '\t' || source[j] == '\n' || source[j] == '\r' ||
                                     source[j] == '>' || source[j] == '+' || source[j] == '~'))
                    {
                        j++;
                    }

                    lastCompoundStart = j;
                    i = j - 1;
                }
            }
        }

        var insertAt = e;
        parenDepth = 0;
        bracketDepth = 0;
        for (var i = lastCompoundStart; i < e; i++)
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
                insertAt = i;
                break;
            }
        }

        sb.Append(source, s, insertAt - s);
        sb.Append(suffix);
        sb.Append(source, insertAt, e - insertAt);
        if (e < end)
        {
            sb.Append(source, e, end - e);
        }
    }

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
