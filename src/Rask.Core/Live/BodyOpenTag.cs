using System.Numerics;

namespace Rask.Core.Live;

/// <summary>
///     Where the page's <c>&lt;body</c> tag opens, in its text or its UTF-8 bytes — the one the browser would open.
/// </summary>
/// <remarks>
///     The first <c>&lt;body</c> anywhere is not that: a head script holding the text (<c>"&lt;body class=x&gt;"</c> in
///     a string), a comment, a <c>&lt;template&gt;</c>, a <c>&lt;title&gt;</c> or a <c>&lt;style&gt;</c> may come
///     first, and the session id, the dev flag and the resume token were then written into that script. A comment and
///     every element whose content is not markup are stepped over whole, as the HTML tokenizer steps over them.
/// </remarks>
internal static class BodyOpenTag
{
    // The elements whose content is text to the tokenizer (raw text, escapable raw text), plus <template> and
    // <noscript>, which a page writes in its head and whose content is never the document's <body>.
    private static readonly string[] Opaque =
        ["script", "style", "title", "textarea", "template", "noscript", "xmp", "iframe", "noembed", "noframes"];

    /// <summary>The index of the <c>&lt;</c> that opens the body, or -1.</summary>
    internal static int IndexIn(ReadOnlySpan<char> html) => IndexIn<char>(html);

    /// <inheritdoc cref="IndexIn(ReadOnlySpan{char})" />
    internal static int IndexIn(ReadOnlySpan<byte> html) => IndexIn<byte>(html);

    private static int IndexIn<T>(ReadOnlySpan<T> html)
        where T : unmanaged, IBinaryInteger<T>
    {
        var lt = T.CreateTruncating('<');
        var at = 0;
        while (at < html.Length)
        {
            var rel = html[at..].IndexOf(lt);
            if (rel < 0)
            {
                return -1;
            }

            at += rel;
            if (Is(html, at + 1, "!--"))
            {
                var close = IndexOf(html, at + 4, "-->");
                if (close < 0)
                {
                    return -1;
                }

                at = close + 3;
                continue;
            }

            if (OpensTag(html, at, "body"))
            {
                return at;
            }

            var after = OpaqueEnd(html, at);
            if (after == -2)
            {
                return -1;
            }

            at = after >= 0 ? after : at + 1;
        }

        return -1;
    }

    // Just past the "</name" that closes an opaque element opening at `at`; -1 when none opens there, -2 when one
    // opens and never closes (nothing after it is markup).
    private static int OpaqueEnd<T>(ReadOnlySpan<T> html, int at)
        where T : unmanaged, IBinaryInteger<T>
    {
        foreach (var name in Opaque)
        {
            if (!OpensTag(html, at, name))
            {
                continue;
            }

            var from = at + 1 + name.Length;
            while (true)
            {
                var close = IndexOf(html, from, "</");
                if (close < 0)
                {
                    return -2;
                }

                if (Is(html, close + 2, name) && Boundary(html, close + 2 + name.Length))
                {
                    return close + 2 + name.Length;
                }

                from = close + 2;
            }
        }

        return -1;
    }

    private static bool OpensTag<T>(ReadOnlySpan<T> html, int at, string name)
        where T : unmanaged, IBinaryInteger<T> =>
        Is(html, at + 1, name) && Boundary(html, at + 1 + name.Length);

    // A tag name ends at the end of the text, a space, '>' or '/'.
    private static bool Boundary<T>(ReadOnlySpan<T> html, int at)
        where T : unmanaged, IBinaryInteger<T>
    {
        if (at >= html.Length)
        {
            return true;
        }

        var c = int.CreateTruncating(html[at]);
        return c is ' ' or '\t' or '\r' or '\n' or '\f' or '>' or '/';
    }

    // Whether `value` (ASCII, lower case) stands at `at`, ignoring ASCII case.
    private static bool Is<T>(ReadOnlySpan<T> html, int at, string value)
        where T : unmanaged, IBinaryInteger<T>
    {
        if (at < 0 || at + value.Length > html.Length)
        {
            return false;
        }

        for (var j = 0; j < value.Length; j++)
        {
            var c = int.CreateTruncating(html[at + j]);
            if (c is >= 'A' and <= 'Z')
            {
                c += 32;
            }

            if (c != value[j])
            {
                return false;
            }
        }

        return true;
    }

    private static int IndexOf<T>(ReadOnlySpan<T> html, int from, string value)
        where T : unmanaged, IBinaryInteger<T>
    {
        var first = T.CreateTruncating(value[0]);
        var at = from;
        while (at < html.Length)
        {
            var rel = html[at..].IndexOf(first);
            if (rel < 0)
            {
                return -1;
            }

            at += rel;
            if (Is(html, at, value))
            {
                return at;
            }

            at++;
        }

        return -1;
    }
}
